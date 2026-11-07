using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Life.Japanese.Mining;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Shows the website what the call analyses found: the calls, everything one call gathered, the user's feedback on a
/// finding, and the trends across all calls.
/// </summary>
internal class CallAnalysisRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(CallAnalysisRequestTransmission);

    private readonly ICallJobDatabase _jobs;
    private readonly IJapaneseDiagnosisDatabase _diagnoses;
    private readonly IPersonProfileDatabase _people;
    private readonly ICallMemory _memory;
    private readonly ILogger<CallAnalysisRequestHandler> _logger;
    private readonly IMinedSentenceDatabase? _mined;
    private readonly string _storageDirectory;
    private readonly IStudyAdvisor? _advisor;

    public CallAnalysisRequestHandler(ICallJobDatabase jobs, IJapaneseDiagnosisDatabase diagnoses, IPersonProfileDatabase people, ICallMemory memory,
        ILogger<CallAnalysisRequestHandler> logger, IMinedSentenceDatabase? mined = null, IOptions<CallAnalysisOptions>? options = null,
        IStudyAdvisor? advisor = null)
    {
        _jobs = jobs;
        _diagnoses = diagnoses;
        _people = people;
        _memory = memory;
        _logger = logger;
        _mined = mined;
        _storageDirectory = options?.Value.StorageDirectory ?? string.Empty;
        _advisor = advisor;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in CallAnalysisRequestHandler");
        CallAnalysisRequestTransmission request = GetTransmission<CallAnalysisRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // A long call's transcript takes a moment to load, and the session reads nothing else until this method returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, request, user)));
        return Task.CompletedTask;
    }

    internal async Task<CallAnalysisResponseTransmission> ProcessAsync(string userId, CallAnalysisRequestTransmission request, User? user = null)
    {
        CallAnalysisResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            List<CallJob> jobs = await _jobs.GetForUser(userId);
            switch (request.Action)
            {
                case CallAnalysisAction.List:
                    await PrepareAsync(jobs, null);
                    response.Calls = [.. CallAnalysisViews.Newest(jobs).Select(CallAnalysisViews.Summary)];
                    break;

                case CallAnalysisAction.Detail:
                {
                    // Looked up among the user's own calls, so nobody reads someone else's
                    CallJob? job = jobs.FirstOrDefault(j => j.Id == request.CallId);
                    if (job == null)
                    {
                        response.Error = "That call analysis does not exist.";
                        break;
                    }
                    JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(userId);
                    await PrepareAsync(jobs, diagnosis);
                    response.Detail = CallAnalysisViews.Detail(job, diagnosis, jobs);
                    break;
                }

                case CallAnalysisAction.Feedback:
                    response.Error = await FeedbackAsync(userId, jobs, request, response);
                    break;

                case CallAnalysisAction.Insights:
                {
                    JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(userId);
                    await PrepareAsync(jobs, diagnosis);
                    response.Insights = CallAnalysisViews.Insights(jobs, diagnosis, request.From, request.To);
                    break;
                }

                case CallAnalysisAction.Delete:
                    response.Error = await DeleteAsync(jobs, request, response);
                    break;

                case CallAnalysisAction.SetFocus:
                {
                    JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(userId);
                    if (diagnosis?.GetIssue(request.IssueId) is not DiagnosisIssue issue)
                    {
                        response.Error = "That struggle is not in your diagnosis.";
                        break;
                    }
                    if (issue.Focus != request.Focus)
                    {
                        issue.Focus = request.Focus;
                        issue.FocusSince = request.Focus ? DateTime.UtcNow : null;
                        diagnosis.Dirty = true;
                        await _diagnoses.SaveDocumentAsync(diagnosis);
                        _logger.LogInformation("Struggle {IssueId} focus set to {Focus} for user {UserId}", issue.Id, issue.Focus, userId);
                    }
                    await PrepareAsync(jobs, diagnosis);
                    response.Insights = CallAnalysisViews.Insights(jobs, diagnosis, request.From, request.To);
                    break;
                }

                case CallAnalysisAction.Advice:
                {
                    if (_advisor == null || user == null)
                    {
                        response.Error = "Written advice is not available here.";
                        break;
                    }
                    JapaneseDiagnosis diagnosis = await _diagnoses.GetOrCreate(userId);
                    await PrepareAsync(jobs, diagnosis);
                    CallInsights insights = CallAnalysisViews.Insights(jobs, diagnosis, request.From, request.To);
                    if (insights.Calls.Length == 0)
                    {
                        response.Error = "There is no analyzed call in this timeframe to base advice on.";
                        response.Insights = insights;
                        break;
                    }
                    diagnosis.Advice = await _advisor.WriteAsync(user, insights, CancellationToken.None);
                    diagnosis.AdviceAt = DateTime.UtcNow;
                    diagnosis.Dirty = true;
                    await _diagnoses.SaveDocumentAsync(diagnosis);
                    insights.WrittenAdvice = diagnosis.Advice;
                    insights.WrittenAdviceAt = diagnosis.AdviceAt;
                    response.Insights = insights;
                    break;
                }

                case CallAnalysisAction.SetDate:
                    response.Error = await SetDateAsync(userId, jobs, request, response);
                    break;

                case CallAnalysisAction.People:
                    response.People = [.. (await _people.GetForUser(userId))
                        .OrderByDescending(p => p.LastTalked ?? DateTime.MinValue)
                        .Select(CallAnalysisViews.Person)];
                    break;

                case CallAnalysisAction.Person:
                    if (await _people.Get(userId, request.Name) is PersonProfile profile)
                    {
                        response.Person = CallAnalysisViews.Person(profile, jobs);
                    }
                    else
                    {
                        response.Error = $"Nothing is known about {request.Name} yet.";
                    }
                    break;

                default:
                    response.Error = "Unknown call analysis request.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Call analysis request {Action} failed for user {UserId}", request.Action, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }
        return response;
    }

    private async Task<string?> FeedbackAsync(string userId, List<CallJob> jobs, CallAnalysisRequestTransmission request, CallAnalysisResponseTransmission response)
    {
        CallJob? job = jobs.FirstOrDefault(j => j.Id == request.CallId);
        if (job == null)
        {
            return "That call analysis does not exist.";
        }
        if (job.Status != CallJobStatus.Completed)
        {
            return "Feedback works once the analysis is finished.";
        }
        JapaneseDiagnosis diagnosis = await _diagnoses.GetOrCreate(userId);
        await PrepareAsync(jobs, diagnosis);
        CallFinding? finding = job.Findings.FirstOrDefault(f => f.Id == request.FindingId);
        if (finding == null || finding.State != FindingState.Kept)
        {
            return "That finding cannot take feedback.";
        }

        DateTime now = DateTime.UtcNow;
        FindingFeedback feedback = (FindingFeedback)(int)request.Feedback;
        if (finding.Kind == FindingKind.Fact)
        {
            // A wrong fact leaves the person's page, the companion's people and her memories
            finding.Feedback = feedback;
            if (feedback == FindingFeedback.NotAMistake)
            {
                await _memory.UndoAsync(job, finding);
            }
            else
            {
                await _memory.ApplyAsync(job, finding, now);
            }
        }
        else
        {
            CallDiagnosis.SetFeedback(diagnosis, job, finding, feedback, now);
        }
        await _diagnoses.SaveDocumentAsync(diagnosis);
        // The report the companion and /calls show follows what the user said
        job.Report = CallAnalysisPipeline.BuildReport(job, SpeechAnalysis.Analyze(job.LearnerText(), job.Register), diagnosis, now);
        job.Touch(now);
        await _jobs.SaveDocumentAsync(job);
        _logger.LogInformation("Call {JobId}: finding {FindingId} marked {Feedback}", job.Id, finding.Id, finding.Feedback);

        response.Detail = CallAnalysisViews.Detail(job, diagnosis, jobs);
        return null;
    }

    private async Task<string?> DeleteAsync(List<CallJob> jobs, CallAnalysisRequestTransmission request, CallAnalysisResponseTransmission response)
    {
        CallJob? job = jobs.FirstOrDefault(j => j.Id == request.CallId);
        if (job == null)
        {
            return "That call analysis does not exist.";
        }
        string? refusal = null;
        CallRemoval removal = new(_jobs, _diagnoses, _people, _memory, _mined, _storageDirectory, _logger);
        CallRemovalResult? result = await removal.RemoveAsync(job, reason => refusal = reason);
        if (result == null)
        {
            return refusal ?? "That call cannot be deleted.";
        }
        jobs.Remove(job);
        response.Calls = [.. CallAnalysisViews.Newest(jobs).Select(CallAnalysisViews.Summary)];
        response.Message = CallRemovalText(CallAnalysisViews.Name(job), result);
        return null;
    }

    internal static string CallRemovalText(string name, CallRemovalResult result)
    {
        List<string> parts = [];
        if (result.Diagnosis > 0)
        {
            parts.Add($"{result.Diagnosis} entr{(result.Diagnosis == 1 ? "y" : "ies")} in your Japanese diagnosis");
        }
        if (result.Facts > 0)
        {
            parts.Add($"{result.Facts} fact{(result.Facts == 1 ? "" : "s")} about you or your partner");
        }
        if (result.MinedSentences > 0)
        {
            parts.Add($"{result.MinedSentences} mined sentence{(result.MinedSentences == 1 ? "" : "s")}");
        }
        string taken = parts.Count == 0 ? string.Empty : " Taken back: " + string.Join(", ", parts) + ".";
        string files = result.FilesDeleted ? string.Empty : " Its files could not be deleted, or there were none.";
        return $"{name} was deleted.{taken}{files}";
    }

    private async Task<string?> SetDateAsync(string userId, List<CallJob> jobs, CallAnalysisRequestTransmission request, CallAnalysisResponseTransmission response)
    {
        CallJob? job = jobs.FirstOrDefault(j => j.Id == request.CallId);
        if (job == null)
        {
            return "That call analysis does not exist.";
        }
        if (request.Date is not DateTime date || date.Year is < 2000 or >= 2100)
        {
            return "That is not a date.";
        }
        if (!job.IsFinished)
        {
            // The running analysis labels findings with the date; changing it underneath would split them
            return "The date can be changed once the analysis is finished.";
        }

        JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(userId);
        await PrepareAsync(jobs, diagnosis);
        if (await CallJobs.SetDateAsync(job, date, diagnosis, _people))
        {
            if (diagnosis != null)
            {
                await _diagnoses.SaveDocumentAsync(diagnosis);
            }
            if (job.Status == CallJobStatus.Completed)
            {
                job.Report = CallAnalysisPipeline.BuildReport(job, SpeechAnalysis.Analyze(job.LearnerText(), job.Register), diagnosis ?? new JapaneseDiagnosis(), DateTime.UtcNow);
            }
            job.Touch(DateTime.UtcNow);
            await _jobs.SaveDocumentAsync(job);
            _logger.LogInformation("Call {JobId}: date set to {Date:yyyy-MM-dd}", job.Id, date);
        }
        response.Detail = CallAnalysisViews.Detail(job, diagnosis, jobs);
        response.Calls = [.. CallAnalysisViews.Newest(jobs).Select(CallAnalysisViews.Summary)];
        return null;
    }

    /// <summary>
    /// Brings finished calls up to date for free: copies what the diagnosis still holds about calls analyzed before
    /// findings were stored on calls, and computes the local statistics. A call still being analyzed is only computed,
    /// never saved, because the pipeline owns it.
    /// </summary>
    private async Task PrepareAsync(List<CallJob> jobs, JapaneseDiagnosis? diagnosis)
    {
        foreach (CallJob job in jobs)
        {
            bool changed = diagnosis != null && CallDiagnosis.ImportLegacy(job, diagnosis);
            if (job.Segments.Count > 0 && job.Stats?.Version != CallStats.CurrentVersion)
            {
                job.Stats = CallStats.Compute(job);
                changed = true;
            }
            if (changed && job.IsFinished)
            {
                await _jobs.SaveDocumentAsync(job);
            }
        }
    }
}
