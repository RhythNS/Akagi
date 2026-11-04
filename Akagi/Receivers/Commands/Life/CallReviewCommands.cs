namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Tools a call review offers the Learning LLM. They only carry the arguments; the call pipeline checks the quotes and
/// decides what reaches the diagnosis, so nothing a review says is stored unchecked.
/// </summary>
internal abstract class CallReviewToolCommand : LifeCommand
{
    public override Task<Command[]> Execute(Context context) => Task.FromResult(Reply(context, "Noted."));

    protected string Value(string name) => GetArgument(name)?.Trim() ?? string.Empty;

    /// <summary>
    /// Ids given as "a1, [b2] c3".
    /// </summary>
    protected string[] Ids(string name) =>
        [.. Value(name).Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => id.Trim('[', ']', '#'))
            .Where(id => id.Length > 0)];
}

internal class NoteCallMistakeCommand : CallReviewToolCommand
{
    public override string Name => "NoteCallMistake";

    public override string Description => "Note one thing the learner should work on, from their own lines in this part of the call.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Category", $"One of: {string.Join(", ", Enum.GetNames<Akagi.Life.Japanese.IssueCategory>())}.", required: true),
        Text("Pattern", "Short key for the underlying problem, e.g. 'に vs で for location of action'. Reuse a known issue's wording when it is the same problem.", required: true),
        Text("IssueId", "The id of the known issue this is, if it is one of them."),
        Text("Said", "The learner's words, copied exactly from one of their lines.", required: true),
        Text("Better", "What a native speaker would say instead in this situation."),
        Text("Explanation", "One sentence on why, focused on nuance and naturalness.", required: true),
        Integer("Severity", "1 polish, 2 noticeable, 3 confusing or rude.")
    ];

    public string Category => Value("Category");
    public string Pattern => Value("Pattern");
    public string IssueId => Value("IssueId").Trim('[', ']');
    public string Said => Value("Said");
    public string Better => Value("Better");
    public string Explanation => Value("Explanation");
    public int Severity => Math.Clamp(GetIntArgument("Severity") ?? 2, 1, 3);
}

internal class NoteCallCorrectUseCommand : CallReviewToolCommand
{
    public override string Name => "NoteCallCorrectUse";

    public override string Description => "Note that the learner got one of the known issues right in this part.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("IssueId", "The id of the known issue.", required: true),
        Text("Said", "The learner's correct words, copied exactly from one of their lines.", required: true)
    ];

    public string IssueId => Value("IssueId").Trim('[', ']');
    public string Said => Value("Said");
}

internal class NoteCallStrengthCommand : CallReviewToolCommand
{
    public override string Name => "NoteCallStrength";

    public override string Description => "Note something the learner does genuinely well in this part.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Strength", "What they do well, in a few words, e.g. 'natural aizuchi'.", required: true),
        Text("Said", "The learner's words that show it, copied exactly from one of their lines.")
    ];

    public string Strength => Value("Strength");
    public string Said => Value("Said");
}

internal class NoteCallPhraseCommand : CallReviewToolCommand
{
    public override string Name => "NoteCallPhrase";

    public override string Description => "Note a natural expression the partner used that the learner could adopt.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Phrase", "The partner's expression, copied exactly from one of their lines.", required: true),
        Text("Meaning", "What it means and when to use it, in one sentence.", required: true)
    ];

    public string Phrase => Value("Phrase");
    public string Meaning => Value("Meaning");
}

internal class NoteCallSummaryCommand : CallReviewToolCommand
{
    public override string Name => "NoteCallSummary";

    public override string Description => "Sum up what this part of the conversation was about.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Summary", "One or two sentences: the topics and anything later parts need to know.", required: true)
    ];

    public string Summary => Value("Summary");
}

internal class GroupCallFindingsCommand : CallReviewToolCommand
{
    public override string Name => "GroupCallFindings";

    public override string Description => "Merge findings that are the same underlying problem into one issue.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("FindingIds", "The ids of the findings, comma separated. A finding can be alone in its group.", required: true),
        Text("IssueId", "The known issue they belong to, if any."),
        Text("Category", "The category of the issue.", required: true),
        Text("Pattern", "Short canonical key for the problem. For a known issue, its pattern.", required: true),
        Text("Explanation", "One sentence on the problem as a whole.", required: true),
        Integer("Severity", "1 polish, 2 noticeable, 3 confusing or rude.", required: true)
    ];

    public string[] FindingIds => Ids("FindingIds");
    public string IssueId => Value("IssueId").Trim('[', ']');
    public string Category => Value("Category");
    public string Pattern => Value("Pattern");
    public string Explanation => Value("Explanation");
    public int Severity => Math.Clamp(GetIntArgument("Severity") ?? 2, 1, 3);
}

internal class DropCallFindingsCommand : CallReviewToolCommand
{
    public override string Name => "DropCallFindings";

    public override string Description => "Drop findings that should not count.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("FindingIds", "The ids of the findings, comma separated.", required: true),
        Text("Reason", "Why, in a few words the learner will read, e.g. 'natives say this too'.", required: true)
    ];

    public string[] FindingIds => Ids("FindingIds");
    public string Reason => Value("Reason");
}

internal class WriteCallReviewCommand : CallReviewToolCommand
{
    public override string Name => "WriteCallReview";

    public override string Description => "Write the overall review of the call.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Summary", "Two to four honest, balanced sentences on how the learner did in this call.", required: true),
        Text("Priorities", "Up to three things to work on, one per line: what, why it matters, and how to practice it.", required: true)
    ];

    public string Summary => Value("Summary");
    public string[] Priorities => [.. Value("Priorities").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(p => System.Text.RegularExpressions.Regex.Replace(p, @"^\s*(?:[-*•]|\d+[.)])\s*", string.Empty).Trim())
        .Where(p => p.Length > 0)
        .Take(3)];
}
