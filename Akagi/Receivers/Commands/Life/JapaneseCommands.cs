using Akagi.Life.Japanese;

namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Base for the tools that read and update the user's long-term <see cref="JapaneseDiagnosis"/>.
/// </summary>
internal abstract class JapaneseCommand : LifeCommand
{
    protected static Task<JapaneseDiagnosis> LoadDiagnosis(Context context) =>
        context.DatabaseFactory.GetDatabase<IJapaneseDiagnosisDatabase>().GetOrCreate(context.User.Id ?? throw new InvalidOperationException("User has no id."));

    protected static Task SaveDiagnosis(Context context, JapaneseDiagnosis diagnosis) =>
        context.DatabaseFactory.GetDatabase<IJapaneseDiagnosisDatabase>().SaveDocumentAsync(diagnosis);

    internal static IssueCategory? ParseCategory(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string value = text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
        if (Enum.TryParse(value, ignoreCase: true, out IssueCategory parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        return value switch
        {
            "phrasing" or "unnatural" or "naturalness" => IssueCategory.UnnaturalPhrasing,
            "particles" => IssueCategory.Particle,
            "stiff" or "textbook" or "bookish" => IssueCategory.Stiffness,
            "politeness" or "keigo" => IssueCategory.Register,
            "grammar" or "n2" or "pattern" => IssueCategory.GrammarPattern,
            "filler" or "fillers" or "pause" => IssueCategory.Hesitation,
            "crutch" or "habit" => IssueCategory.CrutchWord,
            "structure" or "repetition" or "repetitive" => IssueCategory.RecurringStructure,
            "vocab" or "word" or "wordchoice" => IssueCategory.Vocabulary,
            "reading" or "accent" or "pitch" => IssueCategory.Pronunciation,
            _ => null
        };
    }

    protected static string CategoryList => string.Join(", ", Enum.GetNames<IssueCategory>());
}

internal class RecordJapaneseIssueCommand : JapaneseCommand
{
    public override string Name => "RecordJapaneseIssue";

    public override string Description =>
        "Record a recurring weakness in the user's real Japanese: unnatural phrasing, odd collocations, particle slips, " +
        "textbook stiffness, wrong politeness for the listener, confused N2 grammar, fillers or repetitive sentence frames. " +
        "Repeated issues are merged and tracked over time.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Category", $"One of: {CategoryList}.", required: true),
        Text("Pattern", "Short key for the problem, e.g. 'に vs で for location of action', '～わけにはいかない vs ～ざるを得ない', 'starts sentences with 私は'.", required: true),
        Text("Said", "What the user actually said."),
        Text("Better", "What a native speaker would say instead."),
        Text("Explanation", "One sentence on why, focused on nuance and naturalness."),
        Integer("Severity", "1 minor polish, 2 noticeable, 3 confusing or rude. Default 2."),
        Text("Source", "Where it came from, e.g. 'chat' or 'call with Yuki'.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        IssueCategory? category = ParseCategory(GetArgument("Category"));
        if (category == null)
        {
            return Reply(context, $"Unknown category '{GetArgument("Category")}'. Use one of: {CategoryList}.");
        }

        string pattern = RequireArgument("Pattern");
        if (category == IssueCategory.GrammarPattern && GrammarPatterns.Lookup(pattern) is GrammarPattern known)
        {
            pattern = known.Key;
        }

        JapaneseDiagnosis diagnosis = await LoadDiagnosis(context);
        IssueExample example = new()
        {
            Said = GetArgument("Said") ?? string.Empty,
            Better = GetArgument("Better") ?? string.Empty,
            Source = GetArgument("Source") ?? "chat",
            Time = now
        };
        (DiagnosisIssue issue, bool isNew) = diagnosis.RecordIssue(category.Value, pattern, GetArgument("Explanation") ?? string.Empty,
            example, GetIntArgument("Severity") ?? 2, now);
        await SaveDiagnosis(context, diagnosis);

        return Reply(context, isNew
            ? $"New issue [{issue.Id}] {issue.Category}: {issue.Pattern}."
            : $"Issue [{issue.Id}] {issue.Pattern} seen again ({issue.Count}x, last correct streak broken).");
    }
}

internal class RecordCorrectUsageCommand : JapaneseCommand
{
    public override string Name => "RecordCorrectUsage";

    public override string Description =>
        "Note that the user got a known weak spot right (a pattern, particle or register they used to get wrong). " +
        "Several correct uses in a row mark the issue as improving, so you can praise real progress.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Issue", "Issue id or pattern from the diagnosis.", required: true),
        Text("Example", "What the user said correctly.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        JapaneseDiagnosis diagnosis = await LoadDiagnosis(context);
        DiagnosisIssue? issue = diagnosis.FindIssue(RequireArgument("Issue"));
        if (issue == null)
        {
            return Reply(context, $"No issue matches '{GetArgument("Issue")}'. Use JapaneseDiagnosisReport to see them.");
        }

        IssueStatus before = issue.Status;
        issue.RecordCorrect(DateTime.UtcNow);
        diagnosis.Dirty = true;
        await SaveDiagnosis(context, diagnosis);

        string progress = issue.Status != before ? $" It is now {issue.Status.ToString().ToLowerInvariant()}!" : string.Empty;
        return Reply(context, $"Correct use of {issue.Pattern} ({issue.CorrectStreak} in a row).{progress}");
    }
}

internal class RecordJapaneseStrengthCommand : JapaneseCommand
{
    public override string Name => "RecordJapaneseStrength";

    public override string Description => "Remember something the user does well in Japanese (natural aizuchi, good keigo, rich vocabulary), so feedback stays balanced.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Strength", "The strength in a few words, e.g. 'natural aizuchi with friends'.", required: true)
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        JapaneseDiagnosis diagnosis = await LoadDiagnosis(context);
        DiagnosisStrength strength = diagnosis.RecordStrength(RequireArgument("Strength"), DateTime.UtcNow);
        await SaveDiagnosis(context, diagnosis);
        return Reply(context, strength.Count > 1 ? $"Strength confirmed again ({strength.Count}x): {strength.Text}" : $"Noted strength: {strength.Text}");
    }
}

internal class AnalyzeJapaneseCommand : JapaneseCommand
{
    public override string Name => "AnalyzeJapanese";

    public override string Description =>
        "Measure a piece of the user's Japanese (a message or transcript) without guessing: filler and crutch words, " +
        "polite vs plain endings against the expected register, repeated sentence frames and N2 grammar used. " +
        "Set Record to fold the findings into the diagnosis.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Text", "The user's Japanese.", required: true),
        Text("Register", "Who they were talking to: casual (friends, default), polite or formal."),
        Flag("Record", "Store habit words, register problems and repeated frames in the diagnosis."),
        Text("Source", "Where the text came from, e.g. 'chat' or 'voice note'.")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        ExpectedRegister? register = SpeechAnalysis.ParseRegister(GetArgument("Register"));
        if (register == null)
        {
            return Reply(context, "Register must be casual, polite or formal.");
        }

        SpeechReport report = SpeechAnalysis.Analyze(RequireArgument("Text"), register.Value);
        string output = report.Describe();
        if (GetBoolArgument("Record") == true)
        {
            JapaneseDiagnosis diagnosis = await LoadDiagnosis(context);
            int recorded = SpeechAnalysis.Apply(diagnosis, report, GetArgument("Source") ?? "chat", DateTime.UtcNow);
            await SaveDiagnosis(context, diagnosis);
            output += Environment.NewLine + $"Recorded {recorded} issue(s) in the diagnosis.";
        }
        return Reply(context, output);
    }
}

internal class JapaneseDiagnosisReportCommand : JapaneseCommand
{
    public override string Name => "JapaneseDiagnosisReport";

    public override string Description => "Read the user's long-term Japanese diagnosis: focus areas with examples, what is improving, strengths and trends.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Category", "Only issues of this category."),
        Integer("Count", "How many focus areas to show (default 6).")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        DateTime now = DateTime.UtcNow;
        JapaneseDiagnosis diagnosis = await LoadDiagnosis(context);
        if (!diagnosis.HasData)
        {
            return Reply(context, "No diagnosis yet. Record issues as you notice them, or analyze a call.");
        }

        if (ParseCategory(GetArgument("Category")) is IssueCategory category)
        {
            List<DiagnosisIssue> issues = [.. diagnosis.Issues.Where(i => i.Category == category).OrderByDescending(i => i.Weight(now))];
            return Reply(context, issues.Count == 0
                ? $"No {category} issues recorded."
                : string.Join(Environment.NewLine, issues.Select(i => i.Describe(withExamples: true))));
        }

        return Reply(context, diagnosis.Describe(now, Math.Clamp(GetIntArgument("Count") ?? 6, 1, 20), withExamples: true));
    }
}
