using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Shows the website what the call analyses found: the calls, and for one call its report, the struggles it added to the
/// diagnosis, the local speech measurements and the transcript.
/// </summary>
internal partial class CallAnalysisRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(CallAnalysisRequestTransmission);

    private readonly ICallJobDatabase _jobs;
    private readonly IJapaneseDiagnosisDatabase _diagnoses;
    private readonly ILogger<CallAnalysisRequestHandler> _logger;

    public CallAnalysisRequestHandler(ICallJobDatabase jobs, IJapaneseDiagnosisDatabase diagnoses, ILogger<CallAnalysisRequestHandler> logger)
    {
        _jobs = jobs;
        _diagnoses = diagnoses;
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
                    response.Calls = [.. jobs.OrderByDescending(j => j.RecordedAt ?? j.CreatedAt).ThenByDescending(j => j.CreatedAt).Select(ToSummary)];
                    break;

                case CallAnalysisAction.Detail:
                    // Looked up among the user's own calls, so nobody reads someone else's
                    CallJob? job = jobs.FirstOrDefault(j => j.Id == request.CallId);
                    if (job == null)
                    {
                        response.Error = "That call analysis does not exist.";
                        break;
                    }
                    response.Detail = ToDetail(job, await _diagnoses.GetForUser(userId));
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

    internal static CallAnalysisSummary ToSummary(CallJob job) => new()
    {
        Id = job.Id!,
        Title = job.Title,
        Partner = job.Partner,
        State = (CallAnalysisState)(int)job.Status,
        RecordedAt = job.RecordedAt,
        CreatedAt = job.CreatedAt,
        DurationSeconds = job.DurationSeconds,
        IssueCount = job.Chunks.Sum(c => c.IssuesRecorded),
        ChunksAnalyzed = job.Chunks.Count(c => c.Analyzed),
        ChunkCount = job.Chunks.Count,
        Error = job.Error
    };

    internal static CallAnalysisDetail ToDetail(CallJob job, JapaneseDiagnosis? diagnosis)
    {
        CallAnalysisDetail detail = new()
        {
            Summary = ToSummary(job),
            Register = job.Register.ToString().ToLowerInvariant(),
            Report = job.Report,
            Delivered = job.Delivered,
            Transcript = [.. job.Segments.Select(s => new CallTranscriptLine
            {
                Start = s.Start,
                Speaker = s.Speaker,
                Text = s.Text,
                Disputed = s.Disputed,
                IsLearner = IsLearner(job, s)
            })]
        };

        if (diagnosis != null)
        {
            detail.Struggles = [.. Struggles(job, diagnosis)];
            DateTime until = job.IsFinished ? job.UpdatedAt.AddMinutes(1) : DateTime.MaxValue;
            detail.Strengths = [.. diagnosis.Strengths
                .Where(s => s.LastSeen >= job.CreatedAt && s.LastSeen <= until)
                .OrderByDescending(s => s.LastSeen)
                .Select(s => s.Text)];
        }

        if (job.Segments.Count > 0)
        {
            detail.Speech = ToSpeech(SpeechAnalysis.Analyze(job.LearnerText(), job.Register), job.HasLearnerSeparation);
        }
        return detail;
    }

    /// <summary>
    /// The struggles whose evidence came from this call, the most serious and most frequent first.
    /// </summary>
    private static IEnumerable<CallStruggle> Struggles(CallJob job, JapaneseDiagnosis diagnosis)
    {
        string source = job.SourceLabel;
        return diagnosis.Issues
            .Select(issue => (Issue: issue, Examples: issue.Examples.Where(e => e.Source == source).ToList()))
            .Where(found => found.Examples.Count > 0)
            .OrderByDescending(found => found.Issue.Severity * found.Examples.Count)
            .ThenByDescending(found => found.Issue.Severity)
            .Select(found => new CallStruggle
            {
                Id = found.Issue.Id,
                Category = Humanize(found.Issue.Category.ToString()),
                Pattern = found.Issue.Pattern,
                Explanation = found.Issue.Explanation,
                Severity = found.Issue.Severity,
                Status = found.Issue.Status.ToString().ToLowerInvariant(),
                TimesInCall = found.Examples.Count,
                TimesOverall = found.Issue.Count,
                Examples = [.. found.Examples.Select(e => new CallStruggleExample { Said = e.Said, Better = e.Better })]
            });
    }

    private static CallSpeech ToSpeech(SpeechReport report, bool learnerOnly)
    {
        HashSet<string> habits = [.. report.Fillers.Habits.Select(h => h.Form)];
        return new CallSpeech
        {
            LearnerOnly = learnerOnly,
            JapaneseCharacters = report.Fillers.JapaneseCharacters,
            Sentences = report.Register.Sentences,
            FillersPer100 = Math.Round(report.Fillers.Per100, 2),
            Fillers = [.. report.Fillers.Counts.Select(c => new CallFiller { Form = c.Form, Count = c.Count, IsHabit = habits.Contains(c.Form), Example = c.Example })],
            PoliteRatio = report.Register.PoliteRatio,
            Keigo = report.Register.Keigo,
            ExpectedRegister = report.ExpectedRegister.ToString().ToLowerInvariant(),
            RegisterMismatch = report.RegisterMismatch ?? string.Empty,
            RecurringStructures = [.. report.Structures.Select(s => s.Describe())],
            Grammar = [.. report.Patterns.Select(p => new CallGrammarUse { Pattern = p.Pattern.Key, Meaning = p.Pattern.Meaning, Count = p.Count, Example = p.Example })]
        };
    }

    /// <summary>
    /// Matches <see cref="CallJob.LearnerText"/>: a labeled learner, or everything when the recording held only the learner.
    /// </summary>
    private static bool IsLearner(CallJob job, TranscriptSegment segment) =>
        string.IsNullOrWhiteSpace(job.LearnerSpeaker)
            ? job.HasLearnerSeparation
            : string.Equals(segment.Speaker, job.LearnerSpeaker, StringComparison.OrdinalIgnoreCase);

    internal static string Humanize(string name)
    {
        string spaced = WordStart().Replace(name, " $1").Trim();
        return spaced.Length == 0 ? spaced : spaced[0] + spaced[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordStart();
}
