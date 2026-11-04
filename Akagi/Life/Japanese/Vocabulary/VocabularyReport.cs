using Akagi.Bridge.Chat.Models;
using System.Globalization;

namespace Akagi.Life.Japanese.Vocabulary;

/// <summary>
/// One analysis on the vocabulary page. A report reads the filtered calls and returns stat tiles, charts and tables in
/// the generic shapes the page renders, so a new analysis is a new subclass and nothing else: every subclass is
/// registered and listed automatically.
/// </summary>
internal abstract class VocabularyReport
{
    public static readonly string[] GroupOrder = ["Overview", "Words", "Compare", "Form", "Chunks"];

    public abstract string Key { get; }
    public abstract string Title { get; }
    /// <summary>
    /// One of <see cref="GroupOrder"/>.
    /// </summary>
    public abstract string Group { get; }
    public abstract string Description { get; }
    /// <summary>
    /// Where it is listed within its group.
    /// </summary>
    public virtual int Order => 0;
    public virtual VocabOption[] Options => [];

    public abstract VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options);

    public VocabReportInfo Info => new() { Key = Key, Title = Title, Group = Group, Description = Description, Options = Options };

    /// <summary>
    /// The chosen value of an option, or its default.
    /// </summary>
    protected string Option(IReadOnlyDictionary<string, string> options, string key)
    {
        VocabOption? option = Options.FirstOrDefault(o => o.Key == key);
        return options.TryGetValue(key, out string? value) && option?.Choices.Any(c => c.Value == value) == true
            ? value
            : option?.Default ?? string.Empty;
    }

    protected static VocabOption Choice(string key, string label, params (string Value, string Label)[] choices) => new()
    {
        Key = key,
        Label = label,
        Choices = [.. choices.Select(c => new VocabChoice { Value = c.Value, Label = c.Label })],
        Default = choices[0].Value
    };

    protected VocabReportResult Result(string note = "") => new() { Key = Key, Note = note };
}

/// <summary>
/// Building blocks for report output, so every report formats the same way.
/// </summary>
internal static class Vocab
{
    public const string You = "You";
    public const string Partners = "Partners";

    public static VocabColumn Column(string label, VocabColumnKind kind, string hint = "") => new() { Label = label, Kind = kind, Hint = hint };

    public static VocabCell Text(string text) => new() { Text = text };

    public static VocabCell Number(int value) => new() { Text = value.ToString(CultureInfo.InvariantCulture), Sort = value };

    public static VocabCell Decimal(double value, string format = "0.0") => new() { Text = value.ToString(format, CultureInfo.InvariantCulture), Sort = value };

    public static VocabCell Decimal(double? value, string format = "0.0") => value is double known ? Decimal(known, format) : new VocabCell { Text = "-" };

    public static VocabCell Percent(double ratio) => new() { Text = (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%", Sort = ratio };

    public static VocabCell Date(DateTime? date) => date is DateTime known
        ? new VocabCell { Text = known.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Sort = known.Ticks }
        : new VocabCell { Text = "-" };

    public static VocabCell Call(CallTokens call, double? time = null) => new()
    {
        Text = $"{call.Name}, {call.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
        Sort = call.Date.Ticks + (time ?? 0) * TimeSpan.TicksPerSecond,
        Link = call.CallId,
        Time = time
    };

    public static VocabCell Word(string lemma) => new() { Text = lemma, Link = lemma };

    /// <summary>
    /// The columns describing a word: the word, its reading, class, meaning and frequency band.
    /// </summary>
    public static VocabColumn[] WordColumns =>
    [
        Column("Word", VocabColumnKind.Word),
        Column("Reading", VocabColumnKind.Text),
        Column("Class", VocabColumnKind.Text),
        Column("Meaning", VocabColumnKind.Text),
        Column("Band", VocabColumnKind.Text, "How common the word is in newspaper Japanese (JMdict); casual speech words often show as uncommon.")
    ];

    public static VocabCell[] WordCells(VocabularyView view, WordCount word)
    {
        WordInfo? info = view.Info(word);
        return
        [
            Word(word.Lemma),
            Text(ShowReading(word.Lemma, word.Reading) ? word.Reading : string.Empty),
            Text(ClassLabel(word.Class)),
            Text(info?.Meaning ?? string.Empty),
            new VocabCell { Text = info?.Band ?? (view.Dictionary.Ready ? "not listed" : string.Empty), Sort = info?.Rank ?? int.MaxValue }
        ];
    }

    /// <summary>
    /// A reading adds something only to a word written with kanji.
    /// </summary>
    public static bool ShowReading(string lemma, string reading) =>
        reading.Length > 0 && reading != lemma && lemma.Any(c => c is >= '一' and <= '鿿' or '々');

    public static string ClassLabel(WordClass wordClass) => wordClass switch
    {
        WordClass.Noun => "noun",
        WordClass.Verb => "verb",
        WordClass.Adjective => "i-adjective",
        WordClass.AdjectivalNoun => "na-adjective",
        WordClass.Adverb => "adverb",
        WordClass.Adnominal => "adnominal",
        WordClass.Conjunction => "conjunction",
        _ => "interjection"
    };

    public static VocabRow Row(params VocabCell[] cells) => new() { Cells = cells };

    public static VocabRow Row(IEnumerable<VocabCell> first, params VocabCell[] rest) => new() { Cells = [.. first, .. rest] };

    public static string Format(double value, string format = "0.0") => value.ToString(format, CultureInfo.InvariantCulture);

    public static string PercentText(double? ratio) => ratio is double known ? (known * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "-";

    public static VocabStat Stat(string label, string value, string hint = "") => new() { Label = label, Value = value, Hint = hint };

    public const string NoPartnersNote = "None of the calls in view has your partner's voice recorded separately, so there is nothing to compare with.";
    public const string DictionaryLoadingNote = "The dictionary is still loading, so meanings and frequency bands are missing for a moment.";
}
