using Akagi.Bridge.Chat.Models;
using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.Life.Study;

namespace Akagi.Learning;

/// <summary>
/// Something the user currently has trouble with and a lesson can work on.
/// </summary>
internal record Struggle(string Id, StruggleKind Kind, string Title, string Detail, double Weight)
{
    public const string JapanesePrefix = "jp:";
    public const string GrammarPrefix = "gr:";
    public const string VocabularyPrefix = "vo:";
    public const string LessonPrefix = "le:";
}

/// <summary>
/// Gathers the user's weak spots from the Japanese diagnosis, the study profile and the misses of earlier lessons.
/// </summary>
internal static class StruggleFinder
{
    public const int MaxStruggles = 12;
    public const int LessonsToScan = 30;
    private const int MaxPerSource = 6;
    private const int ShakyBox = 2;
    private static readonly TimeSpan HalfLife = DiagnosisIssue.WeightHalfLife;

    public static List<Struggle> Find(JapaneseDiagnosis? diagnosis, LifeProfile profile, IEnumerable<Lesson> lessons, DateTime nowUtc)
    {
        List<Struggle> struggles = [];
        if (diagnosis != null)
        {
            struggles.AddRange(diagnosis.Focus(MaxPerSource, nowUtc).Select(issue => FromIssue(issue, nowUtc)));
        }
        struggles.AddRange(profile.Study.Grammar
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.LastSeen)
            .Take(MaxPerSource)
            .Select(g => FromGrammar(g, nowUtc)));
        struggles.AddRange(profile.Study.Vocabulary
            .Where(v => v.Seen > 0 && v.Box <= ShakyBox && v.IsDue(nowUtc))
            .OrderBy(v => v.Box)
            .ThenBy(v => v.NextReviewAt)
            .Take(MaxPerSource)
            .Select(FromVocabulary));
        struggles.AddRange(FromLessons(lessons, nowUtc));

        return [.. struggles.OrderByDescending(s => s.Weight).Take(MaxStruggles)];
    }

    /// <summary>
    /// The struggles a lesson should work on: the chosen ones, or the most pressing ones.
    /// </summary>
    public static List<Struggle> Select(IReadOnlyList<Struggle> struggles, IReadOnlyCollection<string> focusIds, int count)
    {
        if (focusIds.Count == 0)
        {
            return [.. struggles.Take(count)];
        }
        return [.. struggles.Where(s => focusIds.Contains(s.Id, StringComparer.OrdinalIgnoreCase))];
    }

    private static Struggle FromIssue(DiagnosisIssue issue, DateTime nowUtc)
    {
        IssueExample? example = issue.Examples.LastOrDefault();
        string instead = example == null ? string.Empty : $" Said: {example.Said} Better: {example.Better}";
        return new Struggle(Struggle.JapanesePrefix + issue.Id, StruggleKind.Japanese, issue.Pattern,
            (issue.Explanation + instead).Trim(), issue.Weight(nowUtc));
    }

    private static Struggle FromGrammar(GrammarMistake mistake, DateTime nowUtc) =>
        new(Struggle.GrammarPrefix + mistake.Pattern, StruggleKind.Grammar, mistake.Pattern, mistake.Explanation,
            Pressure(mistake.Count, mistake.LastSeen, nowUtc));

    private static Struggle FromVocabulary(VocabItem item) =>
        new(Struggle.VocabularyPrefix + item.Term, StruggleKind.Vocabulary, item.Term,
            string.Join(" - ", new[] { item.Reading, item.Meaning }.Where(t => t.Length > 0)), 1.5);

    /// <summary>
    /// A concept stays a weak spot until its latest answer was correct. Questions tied to the diagnosis or the
    /// vocabulary are tracked there instead.
    /// </summary>
    private static List<Struggle> FromLessons(IEnumerable<Lesson> lessons, DateTime nowUtc)
    {
        Dictionary<string, (string Concept, string Topic, int Misses, DateTime Last)> open = [];
        IEnumerable<Lesson> scanned = lessons
            .Where(l => l.Status == LessonStatus.Finished)
            .OrderByDescending(l => l.CreatedAt)
            .Take(LessonsToScan)
            .Reverse();

        foreach (Lesson lesson in scanned)
        {
            foreach (LessonQuestion question in lesson.Questions.Where(q => q.IsAnswered && TracksConcept(q)))
            {
                string key = ConceptKey(question);
                if (question.Verdict == LessonVerdict.Correct)
                {
                    open.Remove(key);
                    continue;
                }
                int misses = open.TryGetValue(key, out var known) ? known.Misses : 0;
                open[key] = (question.Concept, lesson.Topic, misses + 1, question.AnsweredAt ?? lesson.CreatedAt);
            }
        }

        return [.. open.Select(pair => new Struggle(Struggle.LessonPrefix + pair.Key, StruggleKind.Lesson, pair.Value.Concept,
            $"Missed {pair.Value.Misses}x in lessons on {pair.Value.Topic}", Pressure(pair.Value.Misses, pair.Value.Last, nowUtc)))];
    }

    private static bool TracksConcept(LessonQuestion question) =>
        question.FocusId.Length == 0 || question.FocusId.StartsWith(Struggle.LessonPrefix, StringComparison.Ordinal);

    private static string ConceptKey(LessonQuestion question) =>
        question.FocusId.Length > 0 ? question.FocusId[Struggle.LessonPrefix.Length..] : NormalizeConcept(question.Concept);

    internal static string NormalizeConcept(string concept) => concept.Trim().ToLowerInvariant();

    private static double Pressure(int count, DateTime lastSeen, DateTime nowUtc)
    {
        double recency = Math.Pow(0.5, Math.Max(0, (nowUtc - lastSeen).TotalDays) / HalfLife.TotalDays);
        return 2 * (1 + Math.Log(Math.Max(1, count))) * recency;
    }
}
