using System.Globalization;
using System.Text;

namespace Akagi.Life.Japanese;

/// <summary>
/// Everything the local analyzers find in a stretch of the user's Japanese, without any LLM.
/// </summary>
internal class SpeechReport
{
    public required FillerReport Fillers { get; init; }
    public required RegisterReport Register { get; init; }
    public required IReadOnlyList<PatternMatch> Patterns { get; init; }
    public required IReadOnlyList<RepeatedStructure> Structures { get; init; }
    public required ExpectedRegister ExpectedRegister { get; init; }

    public string? RegisterMismatch => Register.Mismatch(ExpectedRegister);

    public string Describe()
    {
        StringBuilder text = new();
        text.AppendLine($"{Fillers.JapaneseCharacters} Japanese characters, {Register.Sentences} sentences.");

        text.Append($"Fillers: {Fillers.Per100.ToString("0.0", CultureInfo.InvariantCulture)} per 100 characters");
        if (Fillers.Counts.Count > 0)
        {
            text.Append(" (" + string.Join(", ", Fillers.Counts.Take(6).Select(c => $"{c.Form} {c.Count}x")) + ")");
        }
        text.AppendLine(".");
        List<FillerCount> habits = [.. Fillers.Habits];
        if (habits.Count > 0)
        {
            text.AppendLine("Habit words: " + string.Join(", ", habits.Select(h => $"{h.Form} ({h.Count}x, e.g. '{h.Example}')")));
        }

        text.AppendLine($"Register: {Register.PoliteRatio.ToString("P0", CultureInfo.InvariantCulture)} polite endings, {Register.Keigo} keigo forms, " +
                        $"expected {ExpectedRegister.ToString().ToLowerInvariant()}.");
        if (RegisterMismatch is string mismatch)
        {
            text.AppendLine("Register problem: " + mismatch);
        }

        foreach (RepeatedStructure structure in Structures.Take(3))
        {
            text.AppendLine("Recurring structure: " + structure.Describe());
        }

        text.AppendLine(Patterns.Count > 0
            ? "N2 grammar used: " + string.Join(", ", Patterns.Select(p => $"{p.Pattern.Key} {p.Count}x"))
            : "No N2 grammar patterns used.");
        return text.ToString().TrimEnd();
    }
}

internal static class SpeechAnalysis
{
    public static SpeechReport Analyze(string text, ExpectedRegister expected)
    {
        return new SpeechReport
        {
            Fillers = FillerAnalyzer.Analyze(text),
            Register = RegisterAnalyzer.Analyze(text),
            Patterns = GrammarPatterns.Find(text),
            Structures = StructureAnalyzer.Analyze(text),
            ExpectedRegister = expected
        };
    }

    public static ExpectedRegister? ParseRegister(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        null or "" or "casual" or "friends" or "friend" or "plain" => ExpectedRegister.Casual,
        "polite" or "desu" or "masu" or "work" => ExpectedRegister.Polite,
        "formal" or "keigo" or "business" => ExpectedRegister.Formal,
        _ => null
    };

    /// <summary>
    /// Folds the local findings into the long-term diagnosis: habit words, register mismatches, recurring frames,
    /// N2 pattern use and a measurement snapshot for trends. Returns how many issues were recorded.
    /// </summary>
    /// <param name="issues">Collects every issue recorded and its example, for callers that keep their own record.</param>
    public static int Apply(JapaneseDiagnosis diagnosis, SpeechReport report, string source, DateTime nowUtc,
        List<(DiagnosisIssue Issue, IssueExample? Example)>? issues = null)
    {
        int recorded = 0;
        void Record(IssueCategory category, string pattern, string explanation, IssueExample? example, int severity)
        {
            (DiagnosisIssue issue, _) = diagnosis.RecordIssue(category, pattern, explanation, example, severity, nowUtc);
            issues?.Add((issue, example));
            recorded++;
        }

        foreach (FillerCount habit in report.Fillers.Habits)
        {
            IssueCategory category = habit.Kind == FillerKind.Hesitation ? IssueCategory.Hesitation : IssueCategory.CrutchWord;
            Record(category, habit.Form, $"Used {habit.Count}x in {source}.",
                new IssueExample { Said = habit.Example, Source = source, Time = nowUtc }, severity: 1);
        }

        if (report.RegisterMismatch is string mismatch)
        {
            string pattern = report.ExpectedRegister switch
            {
                ExpectedRegister.Casual => "too polite with friends",
                ExpectedRegister.Polite => "too casual in polite situations",
                _ => "missing keigo"
            };
            string said = report.ExpectedRegister == ExpectedRegister.Casual
                ? report.Register.PoliteExamples.FirstOrDefault() ?? string.Empty
                : report.Register.PlainExamples.FirstOrDefault() ?? string.Empty;
            Record(IssueCategory.Register, pattern, mismatch,
                new IssueExample { Said = said, Source = source, Time = nowUtc }, severity: 2);
        }

        foreach (RepeatedStructure structure in report.Structures.Take(3))
        {
            string pattern = structure.AtStart ? $"{structure.Text}..." : $"...{structure.Text}";
            Record(IssueCategory.RecurringStructure, pattern, $"In {source} the user {structure.Describe()}.",
                null, severity: 1);
        }

        foreach (PatternMatch match in report.Patterns)
        {
            diagnosis.RecordPatternUse(match.Pattern.Key, match.Count, nowUtc);
        }

        if (report.Fillers.JapaneseCharacters >= 200)
        {
            diagnosis.AddSnapshot(new SpeechSnapshot
            {
                Time = nowUtc,
                Source = source,
                Characters = report.Fillers.JapaneseCharacters,
                FillersPer100 = Math.Round(report.Fillers.Per100, 2),
                PoliteRatio = Math.Round(report.Register.PoliteRatio, 2),
                TopFillers = string.Join(", ", report.Fillers.Counts.Take(3).Select(c => $"{c.Form} {c.Count}x"))
            });
        }
        return recorded;
    }
}
