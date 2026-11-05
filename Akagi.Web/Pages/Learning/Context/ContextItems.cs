using Akagi.Bridge.Chat.Models;
using Akagi.Web.Pages.Learning.Components;

namespace Akagi.Web.Pages.Learning.Context;

/// <summary>
/// One thing in a block the user can pick or drop, such as a mistake or one example of it.
/// </summary>
/// <param name="Id">What the pick is stored under in <see cref="ContextBlock.Picks"/>.</param>
/// <param name="Default">Whether it is in the file until the user decides.</param>
/// <param name="CanQuote">Whether transcript lines around it can be quoted.</param>
public sealed record ContextItem(string Id, string Text, string Detail, bool Default, bool CanQuote, IReadOnlyList<ContextItem> Children);

/// <summary>
/// The items of one section of a block.
/// </summary>
public sealed record ContextItemGroup(string Section, string Title, IReadOnlyList<ContextItem> Items);

/// <summary>
/// The items of each kind of block and how they are told apart, shared by the page and <see cref="ContextWriter"/>.
/// </summary>
public static class ContextItems
{
    /// <summary>
    /// The most transcript lines quoted on each side of an example.
    /// </summary>
    public const int MostAround = 10;

    public static bool CanPick(ContextKind kind) => kind is ContextKind.Call or ContextKind.Insights or ContextKind.Person or ContextKind.Lessons;

    /// <summary>
    /// Whether the block has examples from calls, so transcript lines around them can be quoted.
    /// </summary>
    public static bool CanQuote(ContextKind kind) => kind is ContextKind.Call or ContextKind.Insights or ContextKind.Person;

    public static string Issue(string issueId, string pattern) => "issue:" + (string.IsNullOrEmpty(issueId) ? pattern : issueId);

    public static string Finding(CallFindingView finding) =>
        "f:" + (string.IsNullOrEmpty(finding.Id) ? $"{finding.Kind}:{finding.Time}:{finding.Said}:{finding.Pattern}" : finding.Id);

    public static string Fact(PersonFactView fact) => "fact:" + (string.IsNullOrEmpty(fact.Id) ? $"{fact.CallId}:{fact.Text}" : fact.Id);

    public static string Lesson(LearningLesson lesson) => "lesson:" + lesson.Id;

    public static string Weak(LearningStruggle struggle) => "weak:" + (string.IsNullOrEmpty(struggle.Id) ? struggle.Title : struggle.Id);

    /// <summary>
    /// Findings that count: kept by the analysis and not waved off by the user.
    /// </summary>
    public static IEnumerable<CallFindingView> Counted(IEnumerable<CallFindingView> findings) =>
        findings.Where(f => f.State == CallFindingState.Kept && f.Feedback != CallFindingFeedback.NotAMistake);

    /// <summary>
    /// Where a struggle across calls was seen; just its example when the bot sent no list.
    /// </summary>
    public static IReadOnlyList<InsightOccurrence> Examples(InsightStruggle struggle) =>
        struggle.Occurrences.Length > 0 ? struggle.Occurrences
        : struggle.Example is CallFindingView example && !string.IsNullOrWhiteSpace(example.Said) ? [new InsightOccurrence { Finding = example }]
        : [];

    /// <summary>
    /// Every occurrence when "Every occurrence" is on, otherwise just the struggle's example.
    /// </summary>
    public static bool ExampleDefault(ContextBlock block, InsightStruggle struggle, InsightOccurrence occurrence) =>
        block.Has("occurrences") || (struggle.Example is CallFindingView example && Finding(example) == Finding(occurrence.Finding));

    /// <summary>
    /// The transcript line a finding at <paramref name="time"/> is about: the line said then, or a close one that holds the quote.
    /// </summary>
    public static int LineAt(CallTranscriptLine[] lines, double time, string said)
    {
        int index = Math.Max(0, Array.FindLastIndex(lines, l => l.Start <= time + 0.01));
        if (string.IsNullOrWhiteSpace(said) || lines[index].Text.Contains(said.Trim(), StringComparison.Ordinal))
        {
            return index;
        }
        for (int distance = 1; distance <= 3; distance++)
        {
            foreach (int near in new[] { index + distance, index - distance })
            {
                if (near >= 0 && near < lines.Length && lines[near].Text.Contains(said.Trim(), StringComparison.Ordinal))
                {
                    return near;
                }
            }
        }
        return index;
    }

    /// <summary>
    /// The items of the block's sections that are on, for the user to pick from.
    /// </summary>
    public static IReadOnlyList<ContextItemGroup> For(ContextBlock block, object? data)
    {
        List<ContextItemGroup> groups = [];
        void Add(string section, string title, IEnumerable<ContextItem> items)
        {
            List<ContextItem> list = [.. items];
            if (block.Has(section) && list.Count > 0)
            {
                groups.Add(new ContextItemGroup(section, title, list));
            }
        }

        switch (data)
        {
            case CallAnalysisDetail detail:
                bool transcript = detail.Transcript.Length > 0;
                ContextItem Item(CallFindingView f) => FindingItem(f, transcript);
                Add("struggles", "Mistakes", detail.Struggles.Where(s => s.TimesInCall > 0).Select(s => new ContextItem(
                    Issue(s.IssueId, s.Pattern), s.Pattern, $"{s.Category}, {CallText.SeverityLabel(s.Severity).ToLowerInvariant()}, {s.TimesInCall}x",
                    true, false, [.. Counted(s.Findings).Select(Item)])));
                Add("correct", "Correct uses", Counted(detail.CorrectUses).Select(Item));
                Add("strengths", "What went well", Counted(detail.Strengths).Select(Item));
                Add("phrases", "Phrases", Counted(detail.Phrases).Select(Item));
                Add("coaching", "Coaching moments", Counted(detail.Conversation).Select(Item));
                Add("facts", "People facts", Counted(detail.PartnerFacts).Concat(Counted(detail.LearnerFacts)).Select(Item));
                Add("filtered", "Filtered out", detail.FilteredOut.Select(Item));
                break;

            case CallInsights insights:
                Add("struggles", "Struggles", insights.Struggles.Select(s => new ContextItem(
                    Issue(s.IssueId, s.Pattern), s.Pattern, $"{s.Category}, {CallText.SeverityLabel(s.Severity).ToLowerInvariant()}, {s.Times}x in {s.Calls} call(s)",
                    true, false, [.. Examples(s).Select(o => new ContextItem(
                        Finding(o.Finding), Short(o.Finding), Join(o.CallName, Better(o.Finding)),
                        ExampleDefault(block, s, o), o.Finding.Time != null && o.CallId.Length > 0, []))])));
                break;

            case PersonDetail person:
                IEnumerable<ContextItem> facts = person.Facts.Select(f => new ContextItem(
                    Fact(f), f.Text, Join(f.Topic, f.Date.ToString("yyyy-MM-dd")), true, f.Time != null && f.CallId.Length > 0, []));
                if (block.Has("facts"))
                {
                    Add("facts", "Facts", facts);
                }
                else
                {
                    Add("threads", "Facts", person.Facts.Where(f => f.FollowUp).Select(f => facts.First(i => i.Id == Fact(f))));
                }
                break;

            case LearningOverview overview:
                Add("weak", "Weak spots", overview.Struggles.Select(s => new ContextItem(Weak(s), s.Title, s.Kind.ToString(), true, false, [])));
                Add("lessons", "Lessons", overview.Recent.Select(l => new ContextItem(
                    Lesson(l), l.Topic, $"{l.CreatedAt:yyyy-MM-dd}, {l.Questions.Length} questions", true, false, [])));
                break;
        }
        return groups;
    }

    /// <summary>
    /// Items in the file out of all items, children counted on their own.
    /// </summary>
    public static (int Picked, int All) Count(ContextBlock block, IReadOnlyList<ContextItemGroup> groups)
    {
        int picked = 0;
        int all = 0;
        foreach (ContextItem item in groups.SelectMany(g => g.Items))
        {
            bool on = block.IsPicked(item.Id, item.Default);
            all++;
            picked += on ? 1 : 0;
            foreach (ContextItem child in item.Children)
            {
                all++;
                picked += on && block.IsPicked(child.Id, child.Default) ? 1 : 0;
            }
        }
        return (picked, all);
    }

    private static ContextItem FindingItem(CallFindingView finding, bool transcript) => new(
        Finding(finding), Short(finding),
        Join(finding.Time is double time ? CallText.Timestamp(time) : string.Empty, Better(finding)),
        true, transcript && finding.Time != null, []);

    private static string Short(CallFindingView finding) =>
        string.IsNullOrWhiteSpace(finding.Said) ? finding.Pattern : finding.Said;

    private static string Better(CallFindingView finding) =>
        string.IsNullOrWhiteSpace(finding.Better) ? string.Empty : $"better: {finding.Better}";

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
