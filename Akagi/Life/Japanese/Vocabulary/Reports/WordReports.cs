using Akagi.Bridge.Chat.Models;

namespace Akagi.Life.Japanese.Vocabulary.Reports;

/// <summary>
/// Every word in view with how often each side said it.
/// </summary>
internal class FrequencyReport : VocabularyReport
{
    public const int MaxRows = 2000;

    public override string Key => "frequency";
    public override string Title => "Your words";
    public override string Group => "Words";
    public override string Description => "Every word you said, most used first, next to how often your partners said it.";

    /// <summary>
    /// Whose counts the list is about and sorted by.
    /// </summary>
    protected virtual bool Learner => true;

    public override VocabOption[] Options =>
    [
        Choice("scale", "Counts", ("raw", "Times said"), ("thousand", "Per 1000 words")),
        Choice("scope", "Words", ("theirs", Learner ? "Words you said" : "Words they said"), ("all", "All words in view"))
    ];

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        bool perThousand = Option(options, "scale") == "thousand";
        bool all = Option(options, "scope") == "all";
        int mine = view.TotalWords(true);
        int theirs = view.TotalWords(false);
        List<WordCount> words = [.. view.Listed
            .Where(w => all || (Learner ? w.Learner : w.Partner) > 0)
            .OrderByDescending(w => Learner ? w.Learner : w.Partner)
            .ThenByDescending(w => w.Total)
            .Take(MaxRows)];

        VocabCell Count(int count, int total) => perThousand ? Vocab.Decimal(VocabularyView.PerThousand(count, total), "0.00") : Vocab.Number(count);
        VocabColumnKind kind = perThousand ? VocabColumnKind.Decimal : VocabColumnKind.Number;
        string suffix = perThousand ? " / 1000" : string.Empty;

        VocabReportResult result = Result(view.Dictionary.Ready ? string.Empty : Vocab.DictionaryLoadingNote);
        result.Stats =
        [
            Vocab.Stat(Learner ? "Different words you used" : "Different words your partners used", words.Count.ToString()),
            Vocab.Stat(Learner ? "Your words" : "Their words", (Learner ? mine : theirs).ToString())
        ];
        result.Tables =
        [
            new VocabTable
            {
                Title = Learner ? "Your words" : "Your partners' words",
                Columns =
                [
                    .. Vocab.WordColumns,
                    Vocab.Column(Vocab.You + suffix, kind),
                    Vocab.Column(Vocab.Partners + suffix, kind),
                    Vocab.Column(Learner ? "Your calls" : "Their calls", VocabColumnKind.Number, "In how many calls it was said."),
                    Vocab.Column(Learner ? "First said" : "First heard", VocabColumnKind.Date)
                ],
                Rows = [.. words.Select(w => Vocab.Row(Vocab.WordCells(view, w),
                    Count(w.Learner, mine),
                    Count(w.Partner, theirs),
                    Vocab.Number(Learner ? w.LearnerCalls : w.PartnerCalls),
                    Vocab.Date((Learner ? w.FirstSaid : w.FirstHeard)?.Call.Date)))],
                SortColumn = Learner ? 5 : 6,
                Descending = true
            }
        ];
        return result;
    }
}

internal class PartnerFrequencyReport : FrequencyReport
{
    public override string Key => "partner-words";
    public override string Title => "Your partners' words";
    public override string Description => "Every word your partners said, most used first, next to how often you said it. Pick a partner in the filter for one person's words.";
    public override int Order => 1;
    protected override bool Learner => false;
}
