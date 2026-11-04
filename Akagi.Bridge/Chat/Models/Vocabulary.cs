using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// Which calls and words every vocabulary report looks at.
/// </summary>
[MessagePackObject]
public class VocabFilter
{
    /// <summary>
    /// Only calls with this partner; empty for all partners.
    /// </summary>
    [Key(0)]
    public string Partner { get; set; } = string.Empty;
    [Key(1)]
    public DateTime? From { get; set; }
    [Key(2)]
    public DateTime? To { get; set; }
    /// <summary>
    /// Word classes (Noun, Verb, Adjective, AdjectivalNoun, Adverb, Adnominal, Conjunction, Interjection); empty for the
    /// content words: nouns, verbs, adjectives and adverbs.
    /// </summary>
    [Key(3)]
    public string[] Classes { get; set; } = [];
    /// <summary>
    /// Count names of people, places and things (proper nouns), which say little about vocabulary.
    /// </summary>
    [Key(4)]
    public bool IncludeNames { get; set; }
    /// <summary>
    /// Words said fewer times than this in all are left out of word lists.
    /// </summary>
    [Key(5)]
    public int MinCount { get; set; } = 1;
    /// <summary>
    /// Only words whose form, reading or meaning contains this.
    /// </summary>
    [Key(6)]
    public string Search { get; set; } = string.Empty;
}

[MessagePackObject]
public class VocabChoice
{
    [Key(0)]
    public string Value { get; set; } = string.Empty;
    [Key(1)]
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// A setting of one report, shown as a select.
/// </summary>
[MessagePackObject]
public class VocabOption
{
    [Key(0)]
    public string Key { get; set; } = string.Empty;
    [Key(1)]
    public string Label { get; set; } = string.Empty;
    [Key(2)]
    public VocabChoice[] Choices { get; set; } = [];
    [Key(3)]
    public string Default { get; set; } = string.Empty;
}

[MessagePackObject]
public class VocabReportInfo
{
    [Key(0)]
    public string Key { get; set; } = string.Empty;
    [Key(1)]
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// The section it is listed under: Overview, Words, Compare, Form, Chunks.
    /// </summary>
    [Key(2)]
    public string Group { get; set; } = string.Empty;
    [Key(3)]
    public string Description { get; set; } = string.Empty;
    [Key(4)]
    public VocabOption[] Options { get; set; } = [];
}

[MessagePackObject]
public class VocabCallInfo
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string Name { get; set; } = string.Empty;
    [Key(2)]
    public DateTime Date { get; set; }
    [Key(3)]
    public string Partner { get; set; } = string.Empty;
}

/// <summary>
/// What the vocabulary page can show: its reports, and the calls and partners to filter by.
/// </summary>
[MessagePackObject]
public class VocabCatalog
{
    [Key(0)]
    public VocabReportInfo[] Reports { get; set; } = [];
    [Key(1)]
    public VocabCallInfo[] Calls { get; set; } = [];
    [Key(2)]
    public string[] Partners { get; set; } = [];
    /// <summary>
    /// The dictionary (meanings, frequency bands) has loaded; it takes a moment after the bot starts.
    /// </summary>
    [Key(3)]
    public bool DictionaryReady { get; set; }
    /// <summary>
    /// Finished calls left out because their voices were not recorded separately.
    /// </summary>
    [Key(4)]
    public int MixedCalls { get; set; }
}

[MessagePackObject]
public class VocabStat
{
    [Key(0)]
    public string Label { get; set; } = string.Empty;
    [Key(1)]
    public string Value { get; set; } = string.Empty;
    [Key(2)]
    public string Hint { get; set; } = string.Empty;
}

[MessagePackObject]
public class VocabPoint
{
    [Key(0)]
    public string Label { get; set; } = string.Empty;
    /// <summary>
    /// One value per series name; null where it was not measured.
    /// </summary>
    [Key(1)]
    public double?[] Values { get; set; } = [];
    [Key(2)]
    public string CallId { get; set; } = string.Empty;
}

public enum VocabSeriesKind
{
    Columns,
    Line
}

public enum VocabUnit
{
    Count,
    /// <summary>Values from 0 to 1, shown as percent.</summary>
    Ratio,
    Decimal,
    Seconds
}

/// <summary>
/// A chart: one or two series (you, your partners) over calls or categories.
/// </summary>
[MessagePackObject]
public class VocabSeries
{
    [Key(0)]
    public string Title { get; set; } = string.Empty;
    [Key(1)]
    public string[] Names { get; set; } = [];
    [Key(2)]
    public VocabPoint[] Points { get; set; } = [];
    [Key(3)]
    public VocabSeriesKind Kind { get; set; }
    [Key(4)]
    public VocabUnit Unit { get; set; }
    [Key(5)]
    public string Hint { get; set; } = string.Empty;
}

public enum VocabColumnKind
{
    Text,
    /// <summary>A Japanese word; clicking it opens the word.</summary>
    Word,
    Number,
    Decimal,
    Percent,
    Date,
    /// <summary>A call; clicking it opens the call, at the row's time when it has one.</summary>
    Call
}

[MessagePackObject]
public class VocabColumn
{
    [Key(0)]
    public string Label { get; set; } = string.Empty;
    [Key(1)]
    public VocabColumnKind Kind { get; set; }
    [Key(2)]
    public string Hint { get; set; } = string.Empty;
}

[MessagePackObject]
public class VocabCell
{
    [Key(0)]
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// The value sorted by, for numbers and dates (ticks).
    /// </summary>
    [Key(1)]
    public double? Sort { get; set; }
    /// <summary>
    /// For a word cell the dictionary form to open, for a call cell the call id.
    /// </summary>
    [Key(2)]
    public string Link { get; set; } = string.Empty;
    /// <summary>
    /// For a call cell: seconds into the call.
    /// </summary>
    [Key(3)]
    public double? Time { get; set; }
}

[MessagePackObject]
public class VocabRow
{
    [Key(0)]
    public VocabCell[] Cells { get; set; } = [];
}

[MessagePackObject]
public class VocabTable
{
    [Key(0)]
    public string Title { get; set; } = string.Empty;
    [Key(1)]
    public string Description { get; set; } = string.Empty;
    [Key(2)]
    public VocabColumn[] Columns { get; set; } = [];
    [Key(3)]
    public VocabRow[] Rows { get; set; } = [];
    [Key(4)]
    public int SortColumn { get; set; }
    [Key(5)]
    public bool Descending { get; set; } = true;
}

/// <summary>
/// What a report found: any mix of stat tiles, charts and tables.
/// </summary>
[MessagePackObject]
public class VocabReportResult
{
    [Key(0)]
    public string Key { get; set; } = string.Empty;
    [Key(1)]
    public string Note { get; set; } = string.Empty;
    [Key(2)]
    public VocabStat[] Stats { get; set; } = [];
    [Key(3)]
    public VocabSeries[] Series { get; set; } = [];
    [Key(4)]
    public VocabTable[] Tables { get; set; } = [];
}

[MessagePackObject]
public class VocabForm
{
    [Key(0)]
    public string Surface { get; set; } = string.Empty;
    [Key(1)]
    public int Learner { get; set; }
    [Key(2)]
    public int Partner { get; set; }
}

[MessagePackObject]
public class VocabLine
{
    [Key(0)]
    public string CallId { get; set; } = string.Empty;
    [Key(1)]
    public string CallName { get; set; } = string.Empty;
    [Key(2)]
    public DateTime Date { get; set; }
    [Key(3)]
    public double Time { get; set; }
    [Key(4)]
    public bool IsLearner { get; set; }
    [Key(5)]
    public string Speaker { get; set; } = string.Empty;
    [Key(6)]
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// The forms of the word in this line, to mark them.
    /// </summary>
    [Key(7)]
    public string[] Marks { get; set; } = [];
}

/// <summary>
/// Everything about one word across the filtered calls.
/// </summary>
[MessagePackObject]
public class VocabWordDetail
{
    [Key(0)]
    public string Lemma { get; set; } = string.Empty;
    [Key(1)]
    public string Reading { get; set; } = string.Empty;
    [Key(2)]
    public string Class { get; set; } = string.Empty;
    [Key(3)]
    public string Meaning { get; set; } = string.Empty;
    [Key(4)]
    public string Band { get; set; } = string.Empty;
    [Key(5)]
    public int Learner { get; set; }
    [Key(6)]
    public int Partner { get; set; }
    [Key(7)]
    public VocabForm[] Forms { get; set; } = [];
    /// <summary>
    /// Uses per call, you and your partners.
    /// </summary>
    [Key(8)]
    public VocabSeries Timeline { get; set; } = new();
    [Key(9)]
    public VocabLine[] Lines { get; set; } = [];
    [Key(10)]
    public DateTime? FirstSaid { get; set; }
    [Key(11)]
    public DateTime? FirstHeard { get; set; }
    /// <summary>
    /// Lines were capped; this many exist.
    /// </summary>
    [Key(12)]
    public int TotalLines { get; set; }
}
