namespace Akagi.Web.Pages.Learning.Context;

/// <summary>
/// What a block of an exported context is made from.
/// </summary>
public enum ContextKind
{
    /// <summary>Text the user writes, e.g. who they are and what they want to ask.</summary>
    Note,
    /// <summary>Speaking insights across all calls.</summary>
    Insights,
    /// <summary>The tutor's weak spots and recent lessons.</summary>
    Lessons,
    /// <summary>One call analysis, by call id.</summary>
    Call,
    /// <summary>Everything known about one person, by name.</summary>
    Person,
    /// <summary>One vocabulary report, by report key.</summary>
    Vocabulary
}

/// <summary>
/// One part of a block that can be left out, such as the transcript of a call.
/// </summary>
public sealed record ContextSection(string Key, string Label, bool On);

/// <summary>
/// One entry of the context the user builds. Only what to export is stored; the text is written from fresh data.
/// </summary>
public sealed class ContextBlock
{
    public const int AllItems = 0;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public ContextKind Kind { get; set; }
    /// <summary>
    /// The call id, the person's name or the vocabulary report key; empty for the other kinds.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>
    /// The heading in the export. Written by the user for a note, taken from the source when it was added otherwise.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// A note's text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Keys of the sections that are exported.
    /// </summary>
    public List<string> Sections { get; set; } = [];
    /// <summary>
    /// False leaves the block out of the export without removing it.
    /// </summary>
    public bool Included { get; set; } = true;
    /// <summary>
    /// The most entries of a long list (words, table rows) to export; <see cref="AllItems"/> for every one.
    /// </summary>
    public int Limit { get; set; } = 50;
    /// <summary>
    /// For a vocabulary report: only calls with this partner; empty for all.
    /// </summary>
    public string Partner { get; set; } = string.Empty;

    public bool Has(string section) => Sections.Contains(section);

    /// <summary>
    /// The block as it starts: every section that is on by default.
    /// </summary>
    public static ContextBlock Create(ContextKind kind, string key, string title) => new()
    {
        Kind = kind,
        Key = key,
        Title = title,
        Sections = [.. ContextSections.For(kind).Where(s => s.On).Select(s => s.Key)]
    };

    /// <summary>
    /// What the loaded data is filed under; blocks with the same source share it.
    /// </summary>
    public string DataKey => Kind switch
    {
        ContextKind.Vocabulary => $"{Kind}:{Key}:{Partner}",
        ContextKind.Call or ContextKind.Person => $"{Kind}:{Key}",
        _ => Kind.ToString()
    };
}

/// <summary>
/// The parts each kind of block can export.
/// </summary>
public static class ContextSections
{
    public static IReadOnlyList<ContextSection> For(ContextKind kind) => kind switch
    {
        ContextKind.Insights =>
        [
            new("overview", "Overview", true),
            new("calls", "Per call", true),
            new("struggles", "Struggles", true),
            new("occurrences", "Every occurrence", false),
            new("words", "My words", true),
            new("heard", "Heard, not said", true),
            new("phrases", "Phrase book", true)
        ],
        ContextKind.Lessons =>
        [
            new("weak", "Weak spots", true),
            new("lessons", "Recent lessons", true),
            new("questions", "Questions and answers", true)
        ],
        ContextKind.Call =>
        [
            new("overview", "Overview", true),
            new("review", "Tutor's review", true),
            new("struggles", "Mistakes", true),
            new("correct", "Correct uses", true),
            new("strengths", "What went well", true),
            new("phrases", "Phrases", true),
            new("coaching", "Coaching", true),
            new("facts", "People facts", true),
            new("speech", "Speech measurements", true),
            new("flow", "Conversation flow", true),
            new("vocabulary", "Vocabulary", false),
            new("filtered", "Filtered out", false),
            new("report", "Report", false),
            new("transcript", "Transcript", false)
        ],
        ContextKind.Person =>
        [
            new("summary", "Summary", true),
            new("threads", "Ask about next time", true),
            new("facts", "Everything known", true),
            new("calls", "Calls", true),
            new("phrases", "Phrases they used", true)
        ],
        ContextKind.Vocabulary =>
        [
            new("stats", "Numbers", true),
            new("charts", "Charts as tables", true),
            new("tables", "Tables", true)
        ],
        _ => []
    };

    /// <summary>
    /// Whether the block has a list long enough for <see cref="ContextBlock.Limit"/> to matter.
    /// </summary>
    public static bool HasLimit(ContextKind kind) => kind is ContextKind.Insights or ContextKind.Call or ContextKind.Vocabulary;
}
