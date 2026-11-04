using Akagi.Bridge.Chat.Models;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using System.Text.RegularExpressions;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Turns stored calls into what the website shows.
/// </summary>
internal static partial class CallAnalysisViews
{
    public const int MaxInsightWords = 300;
    public const int MaxHeardNotSaid = 100;
    public const int MaxInsightStruggles = 60;
    /// <summary>
    /// How often partners must have used a word before it counts as heard but not said.
    /// </summary>
    public const int MinHeard = 2;

    public static IEnumerable<CallJob> Newest(IEnumerable<CallJob> jobs) =>
        jobs.OrderByDescending(Date).ThenByDescending(j => j.CreatedAt);

    private static DateTime Date(CallJob job) => job.RecordedAt ?? job.CreatedAt;

    public static string Name(CallJob job) =>
        !string.IsNullOrWhiteSpace(job.Partner) ? $"Call with {job.Partner}"
        : !string.IsNullOrWhiteSpace(job.Title) ? job.Title
        : "Call";

    public static CallAnalysisSummary Summary(CallJob job) => new()
    {
        Id = job.Id!,
        Title = job.Title,
        Partner = job.Partner,
        State = (CallAnalysisState)(int)job.Status,
        RecordedAt = job.RecordedAt,
        CreatedAt = job.CreatedAt,
        DurationSeconds = job.DurationSeconds,
        IssueCount = job.IssueCount,
        ChunksAnalyzed = job.Chunks.Count(c => c.Analyzed),
        ChunkCount = job.Chunks.Count,
        Error = job.Error,
        LearnerShare = job.Stats?.Separated == true ? job.Stats.LearnerShare : null
    };

    public static CallAnalysisDetail Detail(CallJob job, JapaneseDiagnosis? diagnosis, IReadOnlyCollection<CallJob> allJobs)
    {
        CallAnalysisDetail detail = new()
        {
            Summary = Summary(job),
            Register = job.Register.ToString().ToLowerInvariant(),
            Report = job.Report,
            Delivered = job.Delivered,
            ReviewSummary = job.Review?.Summary ?? string.Empty,
            Priorities = [.. job.Review?.Priorities ?? []],
            Legacy = job.Findings.Any(f => f.Origin == FindingOrigin.Legacy),
            CanGiveFeedback = job.Status == CallJobStatus.Completed,
            Transcript = [.. job.Segments.Select(s => new CallTranscriptLine
            {
                Start = s.Start,
                Speaker = s.Speaker,
                Text = s.Text,
                Disputed = s.Disputed,
                IsLearner = job.HasLearnerSeparation && job.IsLearnerLine(s)
            })]
        };

        detail.Struggles = [.. Struggles(job, diagnosis)];
        detail.CorrectUses = [.. Visible(job, FindingKind.Correct).Select(f => View(f, diagnosis))];
        detail.Strengths = [.. Visible(job, FindingKind.Strength).Select(f => View(f, diagnosis))];
        detail.Phrases = [.. Visible(job, FindingKind.Phrase).Select(f => View(f, diagnosis))];
        detail.CoachingDone = job.Coaching != null;
        detail.FactsDone = job.FactsChecked;
        detail.CoachingSummary = job.Coaching?.Summary ?? string.Empty;
        detail.CoachingTips = [.. job.Coaching?.Tips ?? []];
        // Things to improve first, then what went well, each in call order
        detail.Conversation = [.. Visible(job, FindingKind.Conversation).OrderBy(f => f.WentWell).ThenBy(f => f.Time ?? double.MaxValue).Select(f => View(f, diagnosis))];
        detail.PartnerFacts = [.. Visible(job, FindingKind.Fact).Where(f => f.Subject == FactSubject.Partner).Select(f => View(f, diagnosis))];
        detail.LearnerFacts = [.. Visible(job, FindingKind.Fact).Where(f => f.Subject == FactSubject.Learner).Select(f => View(f, diagnosis))];
        detail.FilteredOut = [.. job.Findings
            .Where(f => f.State is FindingState.Dropped or FindingState.Rejected)
            .OrderBy(f => f.Time ?? double.MaxValue)
            .Select(f => View(f, diagnosis))];

        if (job.Segments.Count > 0)
        {
            detail.Speech = Speech(SpeechAnalysis.Analyze(job.LearnerText(), job.Register), job.HasLearnerSeparation);
        }
        if (job.Stats is CallStats stats)
        {
            detail.Flow = Flow(stats);
            detail.Vocabulary = Vocabulary(stats, EarlierLearnerWords(job, allJobs));
        }
        return detail;
    }

    /// <summary>
    /// Findings shown in their own section: kept ones, and while the analysis runs the ones still waiting.
    /// </summary>
    private static IEnumerable<CallFinding> Visible(CallJob job, FindingKind kind) =>
        job.Findings
            .Where(f => f.Kind == kind && f.State is FindingState.Kept or FindingState.Candidate)
            .OrderBy(f => f.Time ?? double.MaxValue);

    /// <summary>
    /// The mistakes grouped by the issue they count on, the most serious and most frequent first.
    /// </summary>
    private static IEnumerable<CallStruggle> Struggles(CallJob job, JapaneseDiagnosis? diagnosis) =>
        Visible(job, FindingKind.Mistake)
            .GroupBy(f => f.IssueId.Length > 0 ? f.IssueId : DiagnosisIssue.Key(f.Category, f.Pattern))
            .Select(group =>
            {
                List<CallFinding> findings = [.. group];
                CallFinding first = findings[0];
                DiagnosisIssue? issue = diagnosis?.GetIssue(first.IssueId);
                string explanation = issue?.Explanation is { Length: > 0 } known ? known
                    : findings.Select(f => f.IssueExplanation).FirstOrDefault(e => e.Length > 0) ?? first.Explanation;
                return new CallStruggle
                {
                    IssueId = issue?.Id ?? string.Empty,
                    Category = Humanize(first.Category.ToString()),
                    Pattern = issue?.Pattern ?? first.Pattern,
                    Explanation = explanation,
                    Severity = findings.Max(f => f.Severity),
                    Status = issue?.Status.ToString().ToLowerInvariant() ?? string.Empty,
                    TimesInCall = findings.Count(f => f.Counts || f.State == FindingState.Candidate),
                    TimesOverall = issue?.Count,
                    Findings = [.. findings.Select(f => View(f, diagnosis))]
                };
            })
            .OrderByDescending(s => s.Severity * s.TimesInCall)
            .ThenByDescending(s => s.Severity);

    public static CallFindingView View(CallFinding finding, JapaneseDiagnosis? diagnosis) => new()
    {
        Id = finding.Id,
        Kind = (CallFindingKind)(int)finding.Kind,
        Origin = finding.Origin switch
        {
            FindingOrigin.Measurement => "measured",
            FindingOrigin.Legacy => "earlier analysis",
            _ => "review"
        },
        Category = Humanize(finding.Category.ToString()),
        Pattern = finding.Pattern,
        Said = finding.Said,
        Better = finding.Better,
        Explanation = finding.Explanation,
        Severity = finding.Severity,
        Time = finding.Time,
        Quote = finding.Quote switch
        {
            QuoteCheck.Found => CallQuoteState.Found,
            QuoteCheck.Approximate => CallQuoteState.Approximate,
            QuoteCheck.Disputed => CallQuoteState.Possibly,
            QuoteCheck.NotFound => CallQuoteState.NotFound,
            QuoteCheck.OtherSpeaker => CallQuoteState.OtherSpeaker,
            _ => CallQuoteState.NotQuoted
        },
        State = finding.State switch
        {
            FindingState.Kept => CallFindingState.Kept,
            FindingState.Dropped => CallFindingState.Dropped,
            FindingState.Rejected => CallFindingState.Rejected,
            _ => CallFindingState.Pending
        },
        Note = finding.Note,
        Feedback = (CallFindingFeedback)(int)finding.Feedback,
        IssuePattern = finding.Kind == FindingKind.Correct ? diagnosis?.GetIssue(finding.IssueId)?.Pattern ?? string.Empty : string.Empty,
        Aspect = finding.Aspect,
        Japanese = finding.Japanese,
        WentWell = finding.WentWell,
        Context = finding.Context,
        AboutPartner = finding.Subject == FactSubject.Partner,
        FollowUp = finding.FollowUp
    };

    private static CallSpeech Speech(SpeechReport report, bool learnerOnly)
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

    private static CallFlow Flow(CallStats stats) => new()
    {
        Separated = stats.Separated,
        HasPartner = stats.HasPartner,
        LearnerSeconds = Math.Round(stats.LearnerSeconds, 1),
        PartnerSeconds = Math.Round(stats.PartnerSeconds, 1),
        LearnerCharacters = stats.LearnerCharacters,
        PartnerCharacters = stats.PartnerCharacters,
        LearnerTurns = stats.LearnerTurns,
        PartnerTurns = stats.PartnerTurns,
        LearnerCharactersPerTurn = Math.Round(stats.LearnerCharactersPerTurn, 1),
        PartnerCharactersPerTurn = Math.Round(stats.PartnerCharactersPerTurn, 1),
        LongestLearnerTurnSeconds = Math.Round(stats.LongestLearnerTurnSeconds, 1),
        MedianResponseSeconds = stats.MedianResponseSeconds,
        LearnerOverlaps = stats.LearnerOverlaps,
        LongPauses = stats.LongPauses,
        Windows = [.. stats.Windows.Select(w => new CallFlowWindow { Start = w.Start, LearnerSeconds = w.LearnerSeconds, PartnerSeconds = w.PartnerSeconds })],
        LearnerQuestions = stats.LearnerQuestions,
        PartnerQuestions = stats.PartnerQuestions
    };

    private static CallVocabulary Vocabulary(CallStats stats, HashSet<string> earlier) => new()
    {
        Separated = stats.Separated,
        LearnerWords = stats.LearnerWords,
        PartnerWords = stats.PartnerWords,
        LearnerDistinct = stats.LearnerDistinct,
        PartnerDistinct = stats.PartnerDistinct,
        Words = [.. stats.Words.Select(w => new CallWordView
        {
            Lemma = w.Lemma,
            Reading = w.Reading,
            Class = w.Class.ToString(),
            Learner = w.Learner,
            Partner = w.Partner,
            Surfaces = [.. w.Surfaces],
            NewForLearner = w.Learner > 0 && !earlier.Contains(w.Lemma)
        })]
    };

    /// <summary>
    /// Words the learner used in finished calls before this one.
    /// </summary>
    private static HashSet<string> EarlierLearnerWords(CallJob job, IEnumerable<CallJob> jobs)
    {
        DateTime date = Date(job);
        return [.. jobs
            .Where(j => j.Id != job.Id && j.Status == CallJobStatus.Completed && j.Stats is { Separated: true }
                        && (Date(j) < date || (Date(j) == date && j.CreatedAt < job.CreatedAt)))
            .SelectMany(j => j.Stats!.Words.Where(w => w.Learner > 0).Select(w => w.Lemma))];
    }

    public static CallInsights Insights(IReadOnlyCollection<CallJob> jobs, JapaneseDiagnosis? diagnosis)
    {
        List<CallJob> finished = [.. jobs.Where(j => j.Status == CallJobStatus.Completed && j.Stats != null)
            .OrderBy(Date).ThenBy(j => j.CreatedAt)];
        CallInsights insights = new() { MixedCalls = finished.Count(j => !j.Stats!.Separated) };

        Dictionary<string, (CallWord Word, int Learner, int Partner, int Calls, DateTime? First)> words = [];
        List<CallTrendPoint> points = [];
        foreach (CallJob job in finished)
        {
            CallStats stats = job.Stats!;
            int fresh = 0;
            // Without separate voices the partner's words would count as the learner's, so such calls stay out of the
            // vocabulary; they still show as points on the trends
            foreach (CallWord word in stats.Separated ? stats.Words : [])
            {
                int partner = word.Partner;
                if (!words.TryGetValue(word.Lemma, out var total))
                {
                    total = (word, 0, 0, 0, null);
                }
                if (word.Learner > 0 && total.First == null)
                {
                    fresh++;
                    total.First = Date(job);
                }
                total = (total.Word, total.Learner + word.Learner, total.Partner + partner, total.Calls + (word.Learner > 0 ? 1 : 0), total.First);
                words[word.Lemma] = total;
            }

            points.Add(new CallTrendPoint
            {
                CallId = job.Id!,
                Name = Name(job),
                Date = Date(job),
                DurationSeconds = job.DurationSeconds,
                LearnerShare = stats.Separated ? stats.LearnerShare : null,
                MedianResponseSeconds = stats.Separated ? stats.MedianResponseSeconds : null,
                FillersPer100 = job.HasLearnerSeparation ? Math.Round(SpeechAnalysis.Analyze(job.LearnerText(), job.Register).Fillers.Per100, 2) : null,
                LearnerWords = stats.LearnerWords,
                LearnerDistinct = stats.LearnerDistinct,
                NewWords = fresh,
                IssueCount = job.IssueCount,
                LearnerCharactersPerTurn = Math.Round(stats.LearnerCharactersPerTurn, 1),
                Separated = stats.Separated,
                LearnerQuestions = stats.LearnerQuestions
            });
        }

        insights.Calls = [.. points];
        insights.LearnerWords = words.Values.Sum(w => w.Learner);
        insights.LearnerDistinct = words.Values.Count(w => w.Learner > 0);
        insights.Words = [.. words.Values
            .Where(w => w.Learner > 0)
            .OrderByDescending(w => w.Learner)
            .ThenBy(w => w.Word.Lemma, StringComparer.Ordinal)
            .Take(MaxInsightWords)
            .Select(w => Word(w.Word, w.Learner, w.Partner, w.Calls, w.First))];
        insights.HeardNotSaid = [.. words.Values
            .Where(w => w.Learner == 0 && w.Partner >= MinHeard && w.Word.Class != WordClass.Interjection)
            .OrderByDescending(w => w.Partner)
            .ThenBy(w => w.Word.Lemma, StringComparer.Ordinal)
            .Take(MaxHeardNotSaid)
            .Select(w => Word(w.Word, 0, w.Partner, 0, null))];

        insights.Struggles = [.. InsightStruggles(finished, diagnosis)];
        insights.Phrases = [.. finished
            .SelectMany(j => j.Findings.Where(f => f.Kind == FindingKind.Phrase && f.Counts).Select(f => (Job: j, Finding: f)))
            .OrderByDescending(p => Date(p.Job))
            .DistinctBy(p => CallQuotes.Normalize(p.Finding.Said))
            .Select(p => new InsightPhrase { Phrase = p.Finding.Said, Meaning = p.Finding.Better, CallId = p.Job.Id!, CallName = Name(p.Job), Date = Date(p.Job) })];
        return insights;
    }

    private static InsightWord Word(CallWord word, int learner, int partner, int calls, DateTime? first) => new()
    {
        Lemma = word.Lemma,
        Reading = word.Reading,
        Class = word.Class.ToString(),
        Learner = learner,
        Partner = partner,
        Calls = calls,
        FirstUsed = first
    };

    private static IEnumerable<InsightStruggle> InsightStruggles(List<CallJob> jobs, JapaneseDiagnosis? diagnosis)
    {
        var mistakes = jobs.SelectMany(j => j.KeptMistakes.Select(f => (Job: j, Finding: f)));
        Dictionary<string, int> correct = jobs
            .SelectMany(j => j.Findings.Where(f => f.Kind == FindingKind.Correct && f.Counts && f.IssueId.Length > 0))
            .GroupBy(f => f.IssueId)
            .ToDictionary(g => g.Key, g => g.Count());

        return mistakes
            .GroupBy(m => m.Finding.IssueId.Length > 0 ? m.Finding.IssueId : DiagnosisIssue.Key(m.Finding.Category, m.Finding.Pattern))
            .Select(group =>
            {
                CallFinding latest = group.OrderBy(m => Date(m.Job)).ThenBy(m => m.Finding.Time ?? 0).Last().Finding;
                CallFinding example = group.Where(m => m.Finding.Said.Length > 0).Select(m => m.Finding).LastOrDefault() ?? latest;
                DiagnosisIssue? issue = diagnosis?.GetIssue(latest.IssueId);
                return new InsightStruggle
                {
                    IssueId = issue?.Id ?? string.Empty,
                    Category = Humanize(latest.Category.ToString()),
                    Pattern = issue?.Pattern ?? latest.Pattern,
                    Times = group.Count(),
                    Calls = group.Select(m => m.Job.Id).Distinct().Count(),
                    Severity = group.Max(m => m.Finding.Severity),
                    Status = issue?.Status.ToString().ToLowerInvariant() ?? string.Empty,
                    LastSeen = group.Max(m => Date(m.Job)),
                    Example = View(example, diagnosis),
                    CorrectUses = issue != null && correct.TryGetValue(issue.Id, out int right) ? right : 0
                };
            })
            .OrderByDescending(s => s.Severity * s.Times)
            .ThenByDescending(s => s.LastSeen)
            .Take(MaxInsightStruggles);
    }

    public static PersonSummary Person(PersonProfile profile) => new()
    {
        Name = profile.Name,
        Calls = profile.Calls.Count,
        FirstTalked = profile.Calls.Count == 0 ? null : profile.Calls.Min(c => c.Date),
        LastTalked = profile.LastTalked,
        Facts = profile.Facts.Count,
        OpenThreads = profile.Facts.Count(f => f.FollowUp),
        TotalSeconds = profile.Calls.Sum(c => c.DurationSeconds)
    };

    public static PersonDetail Person(PersonProfile profile, IReadOnlyCollection<CallJob> jobs)
    {
        Dictionary<string, CallJob> byId = jobs.Where(j => j.Id != null).ToDictionary(j => j.Id!);
        List<CallJob> theirs = [.. jobs.Where(j => string.Equals(j.Partner.Trim(), profile.Name, StringComparison.OrdinalIgnoreCase))];
        return new PersonDetail
        {
            Summary = Person(profile),
            Facts = [.. profile.Facts
                .OrderByDescending(f => f.Date)
                .ThenBy(f => f.Time ?? 0)
                .Select(f => new PersonFactView
                {
                    Id = f.Id,
                    CallId = f.CallId,
                    CallName = byId.TryGetValue(f.CallId, out CallJob? job) ? Name(job) : "Call",
                    Date = f.Date,
                    Topic = f.Topic,
                    Text = f.Text,
                    Quote = f.Quote,
                    Time = f.Time,
                    FollowUp = f.FollowUp
                })],
            Calls = [.. profile.Calls
                .OrderByDescending(c => c.Date)
                .Select(c => new PersonCallView
                {
                    CallId = c.CallId,
                    Date = c.Date,
                    DurationSeconds = c.DurationSeconds,
                    IssueCount = byId.TryGetValue(c.CallId, out CallJob? job) ? job.IssueCount : 0,
                    LearnerShare = byId.TryGetValue(c.CallId, out CallJob? measured) && measured.Stats?.Separated == true ? measured.Stats.LearnerShare : null,
                    Facts = profile.Facts.Count(f => f.CallId == c.CallId)
                })],
            Phrases = [.. theirs
                .SelectMany(j => j.Findings.Where(f => f.Kind == FindingKind.Phrase && f.Counts).Select(f => (Job: j, Finding: f)))
                .OrderByDescending(p => Date(p.Job))
                .DistinctBy(p => CallQuotes.Normalize(p.Finding.Said))
                .Select(p => new InsightPhrase { Phrase = p.Finding.Said, Meaning = p.Finding.Better, CallId = p.Job.Id!, CallName = Name(p.Job), Date = Date(p.Job) })]
        };
    }

    internal static string Humanize(string name)
    {
        string spaced = WordStart().Replace(name, " $1").Trim();
        return spaced.Length == 0 ? spaced : spaced[0] + spaced[1..].ToLowerInvariant();
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordStart();
}
