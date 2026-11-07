using Akagi.Bridge.Chat.Models;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using System.Globalization;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// The insights across calls: trends within a timeframe, the struggles the user focuses on, and what to work on next.
/// </summary>
internal static partial class CallAnalysisViews
{
    /// <summary>How many of the latest calls count as "recent" for recommendations.</summary>
    public const int RecentCalls = 3;
    public const int MaxFocusAdvice = 3;
    public const int MaxWordAdvice = 3;
    /// <summary>A word must be said this often before it is worth saying less.</summary>
    public const int MinOverusedCount = 15;
    /// <summary>The user must use it this many times more often than partners do.</summary>
    public const double MinOveruseRatio = 3;
    /// <summary>A heard word must have come up this often before it is worth starting to use.</summary>
    public const int MinHeardForAdvice = 4;

    private static readonly WordClass[] UsefulClasses = [WordClass.Verb, WordClass.Adjective, WordClass.AdjectivalNoun, WordClass.Adverb];

    private static List<CallJob> Finished(IEnumerable<CallJob> jobs) =>
        [.. jobs.Where(j => j.Status == CallJobStatus.Completed && j.Stats != null).OrderBy(Date).ThenBy(j => j.CreatedAt)];

    /// <summary>
    /// <paramref name="from"/> is inclusive and <paramref name="to"/> exclusive, both UTC; null leaves that end open.
    /// </summary>
    internal static bool InRange(CallJob job, DateTime? from, DateTime? to) =>
        (from == null || Date(job) >= from.Value) && (to == null || Date(job) < to.Value);

    /// <summary>
    /// Every finished call as a trend point, oldest first. New words count against all earlier calls, also those outside
    /// a timeframe, so "used for the first time" stays true.
    /// </summary>
    internal static List<(CallJob Job, CallTrendPoint Point)> TrendPoints(IEnumerable<CallJob> jobs)
    {
        HashSet<string> seen = [];
        List<(CallJob, CallTrendPoint)> points = [];
        foreach (CallJob job in Finished(jobs))
        {
            CallStats stats = job.Stats!;
            int fresh = 0;
            foreach (CallWord word in stats.Separated ? stats.Words : [])
            {
                if (word.Learner > 0 && seen.Add(word.Lemma))
                {
                    fresh++;
                }
            }
            points.Add((job, new CallTrendPoint
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
            }));
        }
        return points;
    }

    public static CallInsights Insights(IReadOnlyCollection<CallJob> jobs, JapaneseDiagnosis? diagnosis, DateTime? from = null, DateTime? to = null)
    {
        List<(CallJob Job, CallTrendPoint Point)> all = TrendPoints(jobs);
        List<(CallJob Job, CallTrendPoint Point)> shown = [.. all.Where(p => InRange(p.Job, from, to))];
        List<CallJob> finished = [.. shown.Select(p => p.Job)];
        CallInsights insights = new()
        {
            From = from,
            To = to,
            Calls = [.. shown.Select(p => p.Point)],
            MixedCalls = finished.Count(j => !j.Stats!.Separated),
            HiddenCalls = all.Count - shown.Count,
            WrittenAdvice = diagnosis?.Advice ?? string.Empty,
            WrittenAdviceAt = diagnosis?.AdviceAt
        };

        // The first time the learner said a word ever, not just within the timeframe
        Dictionary<string, DateTime> firstUsed = [];
        foreach ((CallJob job, _) in all)
        {
            foreach (CallWord word in job.Stats!.Separated ? job.Stats.Words : [])
            {
                if (word.Learner > 0)
                {
                    firstUsed.TryAdd(word.Lemma, Date(job));
                }
            }
        }

        Dictionary<string, (CallWord Word, int Learner, int Partner, int Calls)> words = [];
        foreach (CallJob job in finished)
        {
            // Without separate voices the partner's words would count as the learner's, so such calls stay out of the
            // vocabulary; they still show as points on the trends
            foreach (CallWord word in job.Stats!.Separated ? job.Stats.Words : [])
            {
                (CallWord Word, int Learner, int Partner, int Calls) total = words.GetValueOrDefault(word.Lemma, (word, 0, 0, 0));
                words[word.Lemma] = (total.Word, total.Learner + word.Learner, total.Partner + word.Partner, total.Calls + (word.Learner > 0 ? 1 : 0));
            }
        }

        insights.LearnerWords = words.Values.Sum(w => w.Learner);
        insights.LearnerDistinct = words.Values.Count(w => w.Learner > 0);
        insights.Words = [.. words.Values
            .Where(w => w.Learner > 0)
            .OrderByDescending(w => w.Learner)
            .ThenBy(w => w.Word.Lemma, StringComparer.Ordinal)
            .Take(MaxInsightWords)
            .Select(w => Word(w.Word, w.Learner, w.Partner, w.Calls, firstUsed.TryGetValue(w.Word.Lemma, out DateTime first) ? first : null))];
        // Heard but never said in any call, not only in the timeframe
        insights.HeardNotSaid = [.. words.Values
            .Where(w => w.Learner == 0 && w.Partner >= MinHeard && w.Word.Class != WordClass.Interjection && !firstUsed.ContainsKey(w.Word.Lemma))
            .OrderByDescending(w => w.Partner)
            .ThenBy(w => w.Word.Lemma, StringComparer.Ordinal)
            .Take(MaxHeardNotSaid)
            .Select(w => Word(w.Word, 0, w.Partner, 0, null))];

        List<InsightStruggle> struggles = [.. InsightStruggles(finished, diagnosis)];
        insights.Struggles = [.. struggles.Take(MaxInsightStruggles)];
        insights.FocusStruggles = [.. FocusStruggles(diagnosis, struggles, finished, Finished(jobs))];
        insights.Phrases = [.. finished
            .SelectMany(j => j.Findings.Where(f => f.Kind == FindingKind.Phrase && f.Counts).Select(f => (Job: j, Finding: f)))
            .OrderByDescending(p => Date(p.Job))
            .DistinctBy(p => CallQuotes.Normalize(p.Finding.Said))
            .Select(p => new InsightPhrase { Phrase = p.Finding.Said, Meaning = p.Finding.Better, CallId = p.Job.Id!, CallName = Name(p.Job), Date = Date(p.Job) })];
        List<IgnoredAdvice> ignored = diagnosis?.IgnoredAdvice ?? [];
        insights.Advice = [.. Advise(finished, struggles, insights.FocusStruggles, words.Values.Select(w => (w.Word, w.Learner, w.Partner)), insights.HeardNotSaid,
            ignored.Select(i => i.Key).ToHashSet())];
        insights.IgnoredAdvice = [.. ignored
            .OrderByDescending(i => i.IgnoredAt)
            .Select(i => new InsightAdvice { Key = i.Key, Kind = i.Kind, Title = i.Title, Word = AdviceWord(i.Key) })];
        return insights;
    }

    /// <summary>
    /// The struggles the user picked, each with its rate per call in the timeframe and its rate before and since focusing
    /// over all calls. A picked struggle that did not happen in the timeframe is listed too: that is the good news.
    /// </summary>
    private static IEnumerable<InsightStruggle> FocusStruggles(JapaneseDiagnosis? diagnosis, List<InsightStruggle> struggles, List<CallJob> shown, List<CallJob> all)
    {
        if (diagnosis == null)
        {
            yield break;
        }
        foreach (DiagnosisIssue issue in diagnosis.Issues.Where(i => i.Focus).OrderBy(i => i.FocusSince ?? DateTime.MinValue))
        {
            InsightStruggle struggle = struggles.FirstOrDefault(s => s.IssueId == issue.Id) ?? new InsightStruggle
            {
                IssueId = issue.Id,
                Category = Humanize(issue.Category.ToString()),
                Pattern = issue.Pattern,
                Severity = issue.Severity,
                Status = issue.Status.ToString().ToLowerInvariant(),
                LastSeen = issue.LastSeen
            };
            struggle.Focus = true;
            struggle.FocusSince = issue.FocusSince;
            struggle.Trend = [.. shown.Select(job => TrendPoint(job, issue.Id))];
            struggle.RateBefore = Rate(all.Where(j => issue.FocusSince == null || Date(j) < issue.FocusSince), issue.Id);
            struggle.RateSince = issue.FocusSince == null ? null : Rate(all.Where(j => Date(j) >= issue.FocusSince), issue.Id);
            yield return struggle;
        }
    }

    private static StruggleTrendPoint TrendPoint(CallJob job, string issueId)
    {
        int times = Times(job, issueId);
        return new StruggleTrendPoint
        {
            CallId = job.Id!,
            Name = Name(job),
            Date = Date(job),
            Times = times,
            Rate = job.HasLearnerSeparation && job.Stats is { LearnerCharacters: > 0 } stats ? Math.Round(times * 1000.0 / stats.LearnerCharacters, 2) : null
        };
    }

    private static int Times(CallJob job, string issueId) => job.KeptMistakes.Count(f => f.IssueId == issueId);

    /// <summary>
    /// Times per 1,000 characters the user spoke over the calls, counting only calls where their voice was on its own.
    /// </summary>
    internal static double? Rate(IEnumerable<CallJob> jobs, string issueId)
    {
        List<CallJob> measured = [.. jobs.Where(j => j.HasLearnerSeparation && j.Stats is { LearnerCharacters: > 0 })];
        int characters = measured.Sum(j => j.Stats!.LearnerCharacters);
        return characters == 0 ? null : Math.Round(measured.Sum(j => Times(j, issueId)) * 1000.0 / characters, 2);
    }

    /// <summary>
    /// What to work on, from the calls in the timeframe: struggles that keep coming back and are not picked yet, how the
    /// picked ones are going, words the user leans on far more than partners do, and words partners use that the user
    /// never says.
    /// </summary>
    internal static IEnumerable<InsightAdvice> Advise(List<CallJob> jobs, List<InsightStruggle> struggles, IEnumerable<InsightStruggle> focus,
        IEnumerable<(CallWord Word, int Learner, int Partner)> words, IEnumerable<InsightWord> heardNotSaid, IReadOnlySet<string>? ignored = null)
    {
        ignored ??= new HashSet<string>();
        List<CallJob> recent = [.. jobs.TakeLast(RecentCalls)];
        HashSet<string> recentIds = [.. recent.Select(j => j.Id!)];

        foreach (InsightStruggle struggle in focus)
        {
            string pattern = struggle.Pattern;
            if (struggle.RateBefore is double before && struggle.RateSince is double since)
            {
                string direction = since < before * 0.8 ? "going down" : since > before * 1.2 ? "going up" : "about the same";
                yield return new InsightAdvice
                {
                    Kind = "progress",
                    Title = $"{pattern}: {direction}",
                    Detail = $"{Number(before)} per 1,000 characters before you focused on it, {Number(since)} since.",
                    IssueId = struggle.IssueId
                };
            }
            else if (struggle.FocusSince != null)
            {
                yield return new InsightAdvice
                {
                    Kind = "progress",
                    Title = $"{pattern}: no call since you picked it",
                    Detail = "The trend shows up after your next analyzed call.",
                    IssueId = struggle.IssueId
                };
            }
        }

        HashSet<string> focused = [.. focus.Select(f => f.IssueId)];
        foreach (var candidate in struggles
            .Where(s => s.IssueId.Length > 0 && !focused.Contains(s.IssueId) && s.Status is "" or "active" && !ignored.Contains(AdviceKey("focus", s.IssueId)))
            .Select(s => (Struggle: s, Recent: s.Occurrences.Count(o => recentIds.Contains(o.CallId))))
            .Where(c => c.Recent >= 2)
            .OrderByDescending(c => c.Struggle.Severity * c.Recent)
            .ThenByDescending(c => c.Struggle.Times)
            .Take(MaxFocusAdvice))
        {
            InsightStruggle struggle = candidate.Struggle;
            string example = struggle.Example is CallFindingView view && view.Said.Length > 0
                ? view.Better.Length > 0 ? $" For example 「{view.Said}」 → 「{view.Better}」." : $" For example 「{view.Said}」."
                : string.Empty;
            yield return new InsightAdvice
            {
                Kind = "focus",
                Title = $"Focus on: {struggle.Pattern}",
                Detail = $"{candidate.Recent}× in your last {recent.Count} call{(recent.Count == 1 ? "" : "s")} ({struggle.Times}× in all), {SeverityWord(struggle.Severity)}.{example}",
                IssueId = struggle.IssueId,
                Key = AdviceKey("focus", struggle.IssueId)
            };
        }

        List<(CallWord Word, int Learner, int Partner)> counted = [.. words];
        int learnerTotal = counted.Sum(w => w.Learner);
        int partnerTotal = counted.Sum(w => w.Partner);
        if (learnerTotal > 0 && partnerTotal > 0)
        {
            foreach ((CallWord word, int learner, int partner, double ratio) in counted
                .Where(w => w.Learner >= MinOverusedCount)
                .Select(w => (w.Word, w.Learner, w.Partner, Ratio: (double)w.Learner / learnerTotal / ((w.Partner + 0.5) / partnerTotal)))
                .Where(w => w.Ratio >= MinOveruseRatio && !ignored.Contains(AdviceKey("use-less", w.Word.Lemma)))
                .OrderByDescending(w => w.Learner * Math.Log(w.Ratio))
                .Take(MaxWordAdvice))
            {
                yield return new InsightAdvice
                {
                    Kind = "use-less",
                    Title = $"Use less: {word.Lemma}",
                    Detail = $"You said it {learner}× ({Number(learner * 1000.0 / learnerTotal)} per 1,000 words), your partners {partner}× ({Number(partner * 1000.0 / partnerTotal)} per 1,000).",
                    Word = word.Lemma,
                    Key = AdviceKey("use-less", word.Lemma)
                };
            }
        }

        foreach (InsightWord word in heardNotSaid
            .Where(w => w.Partner >= MinHeardForAdvice && Enum.TryParse(w.Class, out WordClass wordClass) && UsefulClasses.Contains(wordClass)
                && !ignored.Contains(AdviceKey("use-more", w.Lemma)))
            .Take(MaxWordAdvice))
        {
            yield return new InsightAdvice
            {
                Kind = "use-more",
                Title = $"Try using: {word.Lemma}",
                Detail = $"Your partners said it {word.Partner}×; you never have. You already understand it, so it is an easy one to make your own.",
                Word = word.Lemma,
                Key = AdviceKey("use-more", word.Lemma)
            };
        }
    }

    /// <summary>
    /// What a suggestion is about, to ignore it by: the kind and the struggle or word.
    /// </summary>
    internal static string AdviceKey(string kind, string target) => $"{kind}:{target}";

    /// <summary>
    /// The word of a word suggestion's key, so an ignored one still links to its lines.
    /// </summary>
    private static string AdviceWord(string key) =>
        key.StartsWith("use-", StringComparison.Ordinal) && key.IndexOf(':') is int colon and > 0 ? key[(colon + 1)..] : string.Empty;

    /// <summary>
    /// The numbers the insights follow, for one call next to the average of the user's other finished calls.
    /// </summary>
    public static CallMetric[] Metrics(CallJob job, IReadOnlyCollection<CallJob> jobs)
    {
        List<(CallJob Job, CallTrendPoint Point)> points = TrendPoints(jobs);
        CallTrendPoint? mine = points.FirstOrDefault(p => p.Job.Id == job.Id).Point;
        if (mine == null)
        {
            return [];
        }
        List<CallTrendPoint> others = [.. points.Where(p => p.Job.Id != job.Id).Select(p => p.Point)];
        CallStats stats = job.Stats!;
        double? issuesPer1000 = stats.LearnerCharacters > 0 && job.HasLearnerSeparation ? job.IssueCount * 1000.0 / stats.LearnerCharacters : null;

        List<CallMetric> metrics = [];
        void Add(string key, string label, string hint, string format, bool? higherIsBetter, Func<CallTrendPoint, double?> value)
        {
            List<double> values = [.. others.Select(value).OfType<double>()];
            metrics.Add(new CallMetric
            {
                Key = key,
                Label = label,
                Hint = hint,
                Format = format,
                HigherIsBetter = higherIsBetter,
                Value = value(mine),
                Average = values.Count == 0 ? null : Math.Round(values.Average(), 2),
                AverageOver = values.Count
            });
        }

        Add("share", "Your share of the speaking", "Higher means you carried more of the conversation.", "percent", true, p => p.LearnerShare);
        Add("distinct", "Different words you used", "", "whole", true, p => p.Separated ? p.LearnerDistinct : null);
        Add("new", "Words used for the first time", "Not said in any earlier call.", "whole", true, p => p.Separated ? p.NewWords : null);
        Add("fillers", "Fillers per 100 characters", "Lower is smoother.", "decimal", false, p => p.FillersPer100);
        Add("answer", "Time until you answer", "Median gap after your partner stops talking.", "seconds", false, p => p.MedianResponseSeconds);
        Add("questions", "Questions you asked", "Asking back keeps a conversation going.", "whole", true, p => p.Separated ? p.LearnerQuestions : null);
        Add("perTurn", "Characters per turn", "How much you say each time you speak.", "decimal", true, p => p.Separated ? p.LearnerCharactersPerTurn : null);
        // Mistakes grow with how much was said, so the comparison is per 1,000 characters
        List<double> otherIssues = [.. Finished(jobs)
            .Where(j => j.Id != job.Id && j.HasLearnerSeparation && j.Stats is { LearnerCharacters: > 0 })
            .Select(j => j.IssueCount * 1000.0 / j.Stats!.LearnerCharacters)];
        metrics.Add(new CallMetric
        {
            Key = "issues",
            Label = "Mistakes per 1,000 characters",
            Hint = "Kept mistakes, measured on what you said.",
            Format = "decimal",
            HigherIsBetter = false,
            Value = issuesPer1000 is double rate ? Math.Round(rate, 2) : null,
            Average = otherIssues.Count == 0 ? null : Math.Round(otherIssues.Average(), 2),
            AverageOver = otherIssues.Count
        });
        Add("length", "Length", "", "duration", null, p => p.DurationSeconds);
        return [.. metrics];
    }

    private static string SeverityWord(int severity) => severity switch
    {
        >= 3 => "it gets in the way of being understood",
        2 => "it is noticeable",
        _ => "minor, but frequent"
    };

    private static string Number(double value) => value.ToString(value >= 10 ? "0" : "0.0", CultureInfo.InvariantCulture);
}
