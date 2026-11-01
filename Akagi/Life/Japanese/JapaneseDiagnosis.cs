using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System.Globalization;
using System.Text;

namespace Akagi.Life.Japanese;

/// <summary>
/// What kind of communication problem an issue is. The focus is natural, real-world Japanese rather than test grammar.
/// </summary>
internal enum IssueCategory
{
    /// <summary>Grammatical but not how a native speaker would say it.</summary>
    UnnaturalPhrasing,
    /// <summary>Words that do not go together, e.g. 薬を飲む vs 薬を食べる.</summary>
    Collocation,
    Particle,
    /// <summary>Textbook-like, written-style or overly explicit speech.</summary>
    Stiffness,
    /// <summary>Politeness that does not fit the listener: too stiff with friends or too casual with strangers.</summary>
    Register,
    /// <summary>Confusing or misusing grammar patterns, especially the N2 ones.</summary>
    GrammarPattern,
    /// <summary>Long pauses, restarts and filled pauses.</summary>
    Hesitation,
    /// <summary>The same filler or hedge word used over and over.</summary>
    CrutchWord,
    /// <summary>The same sentence frame used over and over.</summary>
    RecurringStructure,
    Vocabulary,
    Pronunciation,
    Other,
    /// <summary>Misheard or misunderstood what the other person said.</summary>
    Comprehension
}

internal enum IssueStatus
{
    Active,
    /// <summary>Used correctly several times since the last mistake.</summary>
    Improving,
    /// <summary>Correct for a long time; kept so a relapse is noticed.</summary>
    Resolved
}

internal class IssueExample
{
    public string Said { get; set; } = string.Empty;
    public string Better { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime Time { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A recurring struggle area with evidence. Mistakes and correct uses move it between active, improving and resolved.
/// </summary>
internal class DiagnosisIssue
{
    public const int MaxExamples = 5;
    public const int CorrectUsesToImprove = 3;
    public const int CorrectUsesToResolve = 6;
    public static readonly TimeSpan ResolveAfterQuiet = TimeSpan.FromDays(21);
    public static readonly TimeSpan WeightHalfLife = TimeSpan.FromDays(21);

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];
    public IssueCategory Category { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public List<IssueExample> Examples { get; set; } = [];
    public int Count { get; set; }
    /// <summary>Correct uses since the last mistake.</summary>
    public int CorrectStreak { get; set; }
    public int TotalCorrect { get; set; }
    /// <summary>1 (minor polish) to 3 (gets in the way of being understood or sounds rude).</summary>
    public int Severity { get; set; } = 2;
    public IssueStatus Status { get; set; } = IssueStatus.Active;
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public DateTime? LastCorrect { get; set; }

    public void RecordMistake(IssueExample? example, DateTime nowUtc)
    {
        Count++;
        CorrectStreak = 0;
        LastSeen = nowUtc;
        Status = IssueStatus.Active;
        if (example != null && (example.Said.Length > 0 || example.Better.Length > 0))
        {
            Examples.Add(example);
            if (Examples.Count > MaxExamples)
            {
                Examples.RemoveAt(0);
            }
        }
    }

    public void RecordCorrect(DateTime nowUtc)
    {
        CorrectStreak++;
        TotalCorrect++;
        LastCorrect = nowUtc;
        if (CorrectStreak >= CorrectUsesToResolve && nowUtc - LastSeen >= ResolveAfterQuiet)
        {
            Status = IssueStatus.Resolved;
        }
        else if (CorrectStreak >= CorrectUsesToImprove)
        {
            Status = IssueStatus.Improving;
        }
    }

    /// <summary>
    /// How much the issue deserves attention now: severity and frequency, fading with time and progress.
    /// </summary>
    public double Weight(DateTime nowUtc)
    {
        double recency = Math.Pow(0.5, Math.Max(0, (nowUtc - LastSeen).TotalDays) / WeightHalfLife.TotalDays);
        double progress = Status switch
        {
            IssueStatus.Improving => 0.4,
            IssueStatus.Resolved => 0.05,
            _ => 1.0
        };
        return Math.Clamp(Severity, 1, 3) * (1 + Math.Log(Math.Max(1, Count))) * recency * progress;
    }

    public string Describe(bool withExamples)
    {
        StringBuilder text = new();
        text.Append($"[{Id}] {Category}: {Pattern}");
        text.Append($" ({Count}x");
        if (TotalCorrect > 0)
        {
            text.Append($", {TotalCorrect} correct");
        }
        text.Append(Status == IssueStatus.Active ? ")" : $", {Status.ToString().ToLowerInvariant()})");
        if (!string.IsNullOrWhiteSpace(Explanation))
        {
            text.Append($" - {Explanation}");
        }
        if (withExamples && Examples.Count > 0)
        {
            IssueExample latest = Examples[^1];
            text.Append(string.IsNullOrEmpty(latest.Better)
                ? $" e.g. '{latest.Said}'"
                : $" e.g. '{latest.Said}' -> '{latest.Better}'");
        }
        return text.ToString();
    }

    public static string Key(IssueCategory category, string pattern) => $"{category}:{NormalizePattern(pattern)}";

    public static string NormalizePattern(string pattern)
    {
        StringBuilder normalized = new();
        foreach (char c in pattern.Trim().ToLowerInvariant())
        {
            if (c is '～' or '~' or '〜' || char.IsWhiteSpace(c))
            {
                continue;
            }
            normalized.Append(c);
        }
        return normalized.ToString();
    }
}

internal class DiagnosisStrength
{
    public string Text { get; set; } = string.Empty;
    public int Count { get; set; } = 1;
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// How often an N2 grammar pattern shows up in the user's own speech: range they actively use versus avoid.
/// </summary>
internal class PatternUsage
{
    public string Pattern { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Measurements of one analyzed stretch of speech, kept to show trends across calls.
/// </summary>
internal class SpeechSnapshot
{
    public DateTime Time { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = string.Empty;
    public int Characters { get; set; }
    public double FillersPer100 { get; set; }
    public double PoliteRatio { get; set; }
    public string TopFillers { get; set; } = string.Empty;
}

/// <summary>
/// The long-term picture of the user's spoken Japanese: recurring struggle areas, improvements and strengths.
/// One document per user, fed by the tutor's tools and by call analysis.
/// </summary>
internal class JapaneseDiagnosis : Savable
{
    public const int MaxIssues = 150;
    public const int MaxStrengths = 40;
    public const int MaxSnapshots = 120;

    private string _userId = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public List<DiagnosisIssue> Issues { get; set; } = [];
    public List<DiagnosisStrength> Strengths { get; set; } = [];
    public List<PatternUsage> PatternUsage { get; set; } = [];
    public List<SpeechSnapshot> Snapshots { get; set; } = [];

    public (DiagnosisIssue Issue, bool IsNew) RecordIssue(IssueCategory category, string pattern, string explanation, IssueExample? example,
        int severity, DateTime nowUtc)
    {
        string key = DiagnosisIssue.Key(category, pattern);
        DiagnosisIssue? issue = Issues.FirstOrDefault(i => DiagnosisIssue.Key(i.Category, i.Pattern) == key);
        bool isNew = issue == null;
        if (issue == null)
        {
            issue = new DiagnosisIssue { Category = category, Pattern = pattern.Trim(), FirstSeen = nowUtc };
            Issues.Add(issue);
            TrimIssues(nowUtc);
        }
        if (!string.IsNullOrWhiteSpace(explanation))
        {
            issue.Explanation = explanation.Trim();
        }
        issue.Severity = Math.Clamp(severity, 1, 3);
        issue.RecordMistake(example, nowUtc);
        Dirty = true;
        return (issue, isNew);
    }

    public DiagnosisIssue? FindIssue(string idOrPattern)
    {
        string normalized = DiagnosisIssue.NormalizePattern(idOrPattern);
        return Issues.FirstOrDefault(i => string.Equals(i.Id, idOrPattern.Trim().Trim('[', ']'), StringComparison.OrdinalIgnoreCase))
            ?? Issues.FirstOrDefault(i => DiagnosisIssue.NormalizePattern(i.Pattern) == normalized)
            ?? Issues.FirstOrDefault(i => normalized.Length > 1 && DiagnosisIssue.NormalizePattern(i.Pattern).Contains(normalized, StringComparison.Ordinal));
    }

    public DiagnosisStrength RecordStrength(string text, DateTime nowUtc)
    {
        DiagnosisStrength? strength = Strengths.FirstOrDefault(s => string.Equals(s.Text, text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (strength == null)
        {
            strength = new DiagnosisStrength { Text = text.Trim(), FirstSeen = nowUtc, LastSeen = nowUtc, Count = 0 };
            Strengths.Add(strength);
            if (Strengths.Count > MaxStrengths)
            {
                Strengths.Remove(Strengths.OrderBy(s => s.LastSeen).First());
            }
        }
        strength.Count++;
        strength.LastSeen = nowUtc;
        Dirty = true;
        return strength;
    }

    public void RecordPatternUse(string pattern, int count, DateTime nowUtc)
    {
        if (count <= 0)
        {
            return;
        }
        PatternUsage? usage = PatternUsage.FirstOrDefault(u => u.Pattern == pattern);
        if (usage == null)
        {
            usage = new PatternUsage { Pattern = pattern };
            PatternUsage.Add(usage);
        }
        usage.Count += count;
        usage.LastUsed = nowUtc;
        Dirty = true;
    }

    public void AddSnapshot(SpeechSnapshot snapshot)
    {
        Snapshots.Add(snapshot);
        if (Snapshots.Count > MaxSnapshots)
        {
            Snapshots.RemoveAt(0);
        }
        Dirty = true;
    }

    /// <summary>
    /// The issues most worth working on now.
    /// </summary>
    public List<DiagnosisIssue> Focus(int count, DateTime nowUtc) =>
        [.. Issues.Where(i => i.Status != IssueStatus.Resolved).OrderByDescending(i => i.Weight(nowUtc)).Take(count)];

    /// <summary>
    /// Issues that got better within the window, newest progress first.
    /// </summary>
    public List<DiagnosisIssue> Improvements(DateTime nowUtc, TimeSpan window) =>
        [.. Issues.Where(i => i.Status != IssueStatus.Active && i.LastCorrect.HasValue && nowUtc - i.LastCorrect.Value <= window)
            .OrderByDescending(i => i.LastCorrect)];

    public bool HasData => Issues.Count > 0 || Strengths.Count > 0 || Snapshots.Count > 0;

    public string Describe(DateTime nowUtc, int focusCount, bool withExamples)
    {
        StringBuilder text = new();
        List<DiagnosisIssue> focus = Focus(focusCount, nowUtc);
        if (focus.Count > 0)
        {
            text.AppendLine("Focus areas:");
            foreach (DiagnosisIssue issue in focus)
            {
                text.AppendLine("- " + issue.Describe(withExamples));
            }
        }

        List<DiagnosisIssue> improving = Improvements(nowUtc, TimeSpan.FromDays(30));
        if (improving.Count > 0)
        {
            text.AppendLine("Getting better: " + string.Join(", ", improving.Take(5).Select(i => $"{i.Pattern} ({i.Status.ToString().ToLowerInvariant()})")));
        }

        List<DiagnosisStrength> strengths = [.. Strengths.OrderByDescending(s => s.LastSeen).Take(5)];
        if (strengths.Count > 0)
        {
            text.AppendLine("Strengths: " + string.Join("; ", strengths.Select(s => s.Text)));
        }

        List<PatternUsage> used = [.. PatternUsage.OrderByDescending(u => u.Count).Take(6)];
        if (used.Count > 0)
        {
            text.AppendLine("N2 grammar used in real speech: " + string.Join(", ", used.Select(u => $"{u.Pattern} {u.Count}x")));
        }

        string trend = DescribeTrend();
        if (trend.Length > 0)
        {
            text.AppendLine(trend);
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Filler rate of the latest analyzed speech compared with the average of the ones before.
    /// </summary>
    public string DescribeTrend()
    {
        if (Snapshots.Count == 0)
        {
            return string.Empty;
        }
        SpeechSnapshot latest = Snapshots[^1];
        string line = $"Latest speech ({latest.Source}, {latest.Time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}): " +
                      $"{latest.FillersPer100.ToString("0.0", CultureInfo.InvariantCulture)} fillers per 100 characters";
        List<SpeechSnapshot> earlier = [.. Snapshots.SkipLast(1).TakeLast(10)];
        if (earlier.Count > 0)
        {
            double average = earlier.Average(s => s.FillersPer100);
            line += $" (before: {average.ToString("0.0", CultureInfo.InvariantCulture)})";
        }
        return line;
    }

    private void TrimIssues(DateTime nowUtc)
    {
        while (Issues.Count > MaxIssues)
        {
            Issues.Remove(Issues.OrderBy(i => i.Weight(nowUtc)).First());
        }
    }
}

internal interface IJapaneseDiagnosisDatabase : IDatabase<JapaneseDiagnosis>
{
    public Task<JapaneseDiagnosis?> GetForUser(string userId);
    public Task<JapaneseDiagnosis> GetOrCreate(string userId);
}

internal class JapaneseDiagnosisDatabase : Database<JapaneseDiagnosis>, IJapaneseDiagnosisDatabase
{
    public override string CollectionName => "japanese_diagnoses";

    public JapaneseDiagnosisDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is JapaneseDiagnosis;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((JapaneseDiagnosis)savable);

    public async Task<JapaneseDiagnosis?> GetForUser(string userId)
    {
        FilterDefinition<JapaneseDiagnosis> filter = Builders<JapaneseDiagnosis>.Filter.Eq(d => d.UserId, userId);
        return (await GetDocumentsByPredicateAsync(filter)).FirstOrDefault();
    }

    public async Task<JapaneseDiagnosis> GetOrCreate(string userId)
    {
        JapaneseDiagnosis? diagnosis = await GetForUser(userId);
        if (diagnosis != null)
        {
            return diagnosis;
        }
        diagnosis = new JapaneseDiagnosis { UserId = userId };
        await SaveDocumentAsync(diagnosis);
        return diagnosis;
    }
}
