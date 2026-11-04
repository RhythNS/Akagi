using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Users;
using Microsoft.Extensions.Logging;

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

    public CallAnalysisRequestHandler(ICallJobDatabase jobs, IJapaneseDiagnosisDatabase diagnoses, IPersonProfileDatabase people, ICallMemory memory,
        ILogger<CallAnalysisRequestHandler> logger)
    {
        _jobs = jobs;
        _diagnoses = diagnoses;
        _people = people;
        _memory = memory;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in CallAnalysisRequestHandler");
        CallAnalysisRequestTransmission request = GetTransmission<CallAnalysisRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // A long call's transcript takes a moment to load, and the session reads nothing else until this method returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, request)));
        return Task.CompletedTask;
    }

    internal async Task<CallAnalysisResponseTransmission> ProcessAsync(string userId, CallAnalysisRequestTransmission request)
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
                    response.Insights = CallAnalysisViews.Insights(jobs, diagnosis);
                    break;
                }

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
