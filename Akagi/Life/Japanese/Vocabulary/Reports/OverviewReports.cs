using Akagi.Bridge.Chat.Models;

namespace Akagi.Life.Japanese.Vocabulary.Reports;

internal class SummaryReport : VocabularyReport
{
    public override string Key => "summary";
    public override string Title => "At a glance";
    public override string Group => "Overview";
    public override string Description => "How many words you and your partners used, how varied, and how fast.";

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        VocabReportResult result = Result(view.Dictionary.Ready ? string.Empty : Vocab.DictionaryLoadingNote);
        IReadOnlyDictionary<string, WordCount> counts = view.Counts;
        int mine = view.TotalWords(true);
        int theirs = view.TotalWords(false);
        double mySeconds = view.Calls.Sum(c => c.LearnerSeconds);
        double theirSeconds = view.Calls.Sum(c => c.PartnerSeconds);
        int myDistinct = counts.Values.Count(w => w.Learner > 0);
        int theirDistinct = counts.Values.Count(w => w.Partner > 0);

        List<VocabStat> stats =
        [
            Vocab.Stat("Calls", view.Calls.Count.ToString()),
            Vocab.Stat("Your words", mine.ToString(), view.HasPartners ? $"your partners: {theirs}" : string.Empty),
            Vocab.Stat("Different words you used", myDistinct.ToString(), view.HasPartners ? $"your partners: {theirDistinct}" : string.Empty),
            Vocab.Stat("Your variety (MATTR)", Vocab.PercentText(WeightedMattr(view, true)),
                "Share of different words in every stretch of 50 words; it does not fall just because a call was long." +
                (view.HasPartners ? $" Partners: {Vocab.PercentText(WeightedMattr(view, false))}." : string.Empty)),
            Vocab.Stat("Your words per speaking minute", mySeconds > 0 ? Vocab.Format(mine / (mySeconds / 60)) : "-",
                view.HasPartners && theirSeconds > 0 ? $"partners: {Vocab.Format(theirs / (theirSeconds / 60))}" : string.Empty)
        ];
        if (view.Dictionary.Ready && myDistinct > 0)
        {
            int rare = counts.Values.Count(w => w.Learner > 0 && (view.Info(w)?.Rank ?? int.MaxValue) > 10_000);
            stats.Add(Vocab.Stat("Beyond the 10k most common", Vocab.PercentText((double)rare / myDistinct),
                $"{rare} of your different words; casual words count here too, since the bands come from newspapers."));
        }
        result.Stats = [.. stats];

        result.Series =
        [
            new VocabSeries
            {
                Title = "Different words per call",
                Names = view.HasPartners ? [Vocab.You, Vocab.Partners] : [Vocab.You],
                Kind = VocabSeriesKind.Columns,
                Unit = VocabUnit.Count,
                Points = [.. view.Calls.Select(c => new VocabPoint
                {
                    Label = $"{c.Name}, {c.Date:yyyy-MM-dd}",
                    CallId = c.CallId,
                    Values = view.HasPartners
                        ? [Distinct(view, c, true), c.HasPartner ? Distinct(view, c, false) : null]
                        : [Distinct(view, c, true)]
                })]
            }
        ];
        return result;
    }

    private static double Distinct(VocabularyView view, CallTokens call, bool learner) =>
        view.Words(call, learner).Select(t => t.Token.Lemma).Distinct().Count();

    /// <summary>
    /// Each call's MATTR weighted by its number of words.
    /// </summary>
    internal static double? WeightedMattr(VocabularyView view, bool learner)
    {
        double sum = 0;
        int weight = 0;
        foreach (CallTokens call in view.Calls.Where(c => learner || c.HasPartner))
        {
            int words = view.Words(call, learner).Count();
            if (view.Mattr(call, learner) is double mattr && words > 0)
            {
                sum += mattr * words;
                weight += words;
            }
        }
        return weight == 0 ? null : sum / weight;
    }
}

internal class GrowthReport : VocabularyReport
{
    public override string Key => "growth";
    public override string Title => "Growth over time";
    public override string Group => "Overview";
    public override string Description => "Every different word you have used, adding up call by call, and what each call added.";
    public override int Order => 1;

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        VocabReportResult result = Result();
        HashSet<string> mine = [];
        HashSet<string> theirs = [];
        // Words are new when no earlier call at all had them, whatever the filter shows
        HashSet<string> before = [];
        int filteredIndex = 0;
        List<VocabPoint> points = [];
        List<VocabRow> rows = [];

        foreach (CallTokens call in view.AllCalls)
        {
            List<string> said = [.. view.Words(call, true).Select(t => t.Token.Lemma)];
            if (filteredIndex < view.Calls.Count && view.Calls[filteredIndex] == call)
            {
                filteredIndex++;
                int fresh = said.Distinct().Count(l => !before.Contains(l));
                mine.UnionWith(said);
                theirs.UnionWith(view.Words(call, false).Select(t => t.Token.Lemma));
                points.Add(new VocabPoint
                {
                    Label = $"{call.Name}, {call.Date:yyyy-MM-dd}",
                    CallId = call.CallId,
                    Values = view.HasPartners ? [mine.Count, theirs.Count] : [mine.Count]
                });
                rows.Add(Vocab.Row(
                    Vocab.Call(call),
                    Vocab.Number(said.Count),
                    Vocab.Number(said.Distinct().Count()),
                    Vocab.Number(fresh),
                    Vocab.Decimal(view.Mattr(call, true) * 100, "0"),
                    Vocab.Decimal(call.LearnerSeconds > 0 ? said.Count / (call.LearnerSeconds / 60) : null),
                    Vocab.Number(call.HasPartner ? view.Words(call, false).Select(t => t.Token.Lemma).Distinct().Count() : 0)));
            }
            before.UnionWith(said);
        }

        result.Series =
        [
            new VocabSeries
            {
                Title = "Different words so far",
                Hint = "Each point adds the call's new words to everything before it.",
                Names = view.HasPartners ? [Vocab.You, Vocab.Partners] : [Vocab.You],
                Kind = VocabSeriesKind.Line,
                Unit = VocabUnit.Count,
                Points = [.. points]
            }
        ];
        result.Tables =
        [
            new VocabTable
            {
                Title = "Per call",
                Columns =
                [
                    Vocab.Column("Call", VocabColumnKind.Call),
                    Vocab.Column("Your words", VocabColumnKind.Number),
                    Vocab.Column("Different", VocabColumnKind.Number),
                    Vocab.Column("New", VocabColumnKind.Number, "Not used in any earlier call."),
                    Vocab.Column("Variety %", VocabColumnKind.Decimal, "MATTR: share of different words in every stretch of 50 words."),
                    Vocab.Column("Words / min", VocabColumnKind.Decimal, "Your words per minute of your own speaking."),
                    Vocab.Column("Partner's different", VocabColumnKind.Number)
                ],
                Rows = [.. rows],
                SortColumn = 0,
                Descending = true
            }
        ];
        return result;
    }
}
