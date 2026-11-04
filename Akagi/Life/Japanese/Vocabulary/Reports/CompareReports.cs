using Akagi.Bridge.Chat.Models;

namespace Akagi.Life.Japanese.Vocabulary.Reports;

/// <summary>
/// Words one side uses noticeably more than the other, by log-likelihood, so a difference counts only when the numbers
/// make it unlikely to be chance.
/// </summary>
internal abstract class KeynessReport : VocabularyReport
{
    /// <summary>
    /// Significant at p &lt; 0.05.
    /// </summary>
    public const double Threshold = 3.84;
    public const int MinOccurrences = 3;
    public const int MaxRows = 300;

    public override string Group => "Compare";

    /// <summary>
    /// True for words the learner over-uses, false for words the partners use more.
    /// </summary>
    protected abstract bool LearnerSide { get; }

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        if (!view.HasPartners)
        {
            return Result(Vocab.NoPartnersNote);
        }
        int mine = view.TotalWords(true);
        int theirs = view.TotalWords(false);
        bool neverOnly = Option(options, "show") == "never";

        List<(WordCount Word, double Keyness, double Ratio)> key = [.. view.Listed
            .Where(w => (LearnerSide ? w.Learner : w.Partner) >= MinOccurrences)
            .Where(w => !neverOnly || w.Learner == 0)
            .Select(w => (Word: w, Keyness: VocabularyView.Keyness(w.Learner, w.Partner, mine, theirs), Ratio: VocabularyView.Ratio(w.Learner, w.Partner, mine, theirs)))
            .Where(k => LearnerSide ? k.Keyness >= Threshold : k.Keyness <= -Threshold)
            .OrderByDescending(k => Math.Abs(k.Keyness))
            .Take(MaxRows)];

        VocabReportResult result = Result(Note(view));
        result.Stats = [Vocab.Stat(LearnerSide ? "Words you use clearly more" : "Words they use clearly more", key.Count.ToString())];
        result.Tables =
        [
            new VocabTable
            {
                Title = Title,
                Description = "Per 1000 words, so a long monologue does not inflate a count. Keyness above 3.84 is unlikely to be chance (p < 0.05), above 10.83 very unlikely (p < 0.001).",
                Columns =
                [
                    .. Vocab.WordColumns,
                    Vocab.Column("You / 1000", VocabColumnKind.Decimal),
                    Vocab.Column("Partners / 1000", VocabColumnKind.Decimal),
                    Vocab.Column(LearnerSide ? "Times more" : "Times more by them", VocabColumnKind.Decimal),
                    Vocab.Column(Vocab.You, VocabColumnKind.Number),
                    Vocab.Column(Vocab.Partners, VocabColumnKind.Number),
                    Vocab.Column("Keyness", VocabColumnKind.Decimal, "Log-likelihood; the larger, the less likely the difference is chance.")
                ],
                Rows = [.. key.Select(k => Vocab.Row(Vocab.WordCells(view, k.Word),
                    Vocab.Decimal(VocabularyView.PerThousand(k.Word.Learner, mine), "0.00"),
                    Vocab.Decimal(VocabularyView.PerThousand(k.Word.Partner, theirs), "0.00"),
                    Vocab.Decimal(LearnerSide ? k.Ratio : 1 / k.Ratio),
                    Vocab.Number(k.Word.Learner),
                    Vocab.Number(k.Word.Partner),
                    Vocab.Decimal(Math.Abs(k.Keyness))))],
                SortColumn = 10,
                Descending = true
            }
        ];
        return result;
    }

    protected virtual string Note(VocabularyView view) => string.Empty;
}

internal class RelyOnReport : KeynessReport
{
    public override string Key => "rely-on";
    public override string Title => "Words you rely on";
    public override string Description => "Words you use clearly more often than your partners: habits, safe choices and crutch words.";
    protected override bool LearnerSide => true;
}

internal class PartnersUseReport : KeynessReport
{
    public override string Key => "partners-use";
    public override string Title => "Words your partners use more";
    public override string Description => "Words natives use clearly more often than you, including ones you never say: the vocabulary you hear but have not made your own.";
    public override int Order => 1;
    protected override bool LearnerSide => false;

    public override VocabOption[] Options => [Choice("show", "Show", ("all", "All of them"), ("never", "Only words you never said"))];
}

internal class RisingReport : VocabularyReport
{
    public override string Key => "rising";
    public override string Title => "Rising and falling";
    public override string Group => "Compare";
    public override string Description => "Words you use clearly more, or less, in recent calls than before.";
    public override int Order => 2;

    public override VocabOption[] Options =>
    [
        Choice("split", "Recent means", ("last", "The latest call"), ("last3", "The latest 3 calls"), ("month", "The last 30 days"))
    ];

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        List<CallTokens> calls = view.Calls;
        List<CallTokens> recent = Option(options, "split") switch
        {
            "last3" => [.. calls.TakeLast(3)],
            "month" => [.. calls.Where(c => c.Date >= calls.Max(x => x.Date).AddDays(-30))],
            _ => [.. calls.TakeLast(1)]
        };
        List<CallTokens> earlier = [.. calls.Except(recent)];
        if (recent.Count == 0 || earlier.Count == 0)
        {
            return Result("This needs calls both in the recent period and before it.");
        }

        Dictionary<string, int> now = Count(view, recent);
        Dictionary<string, int> then = Count(view, earlier);
        int nowTotal = now.Values.Sum();
        int thenTotal = then.Values.Sum();
        IReadOnlyDictionary<string, WordCount> words = view.Counts;
        List<(WordCount Word, int Now, int Then, double Keyness)> changes = [.. view.Listed
            .Select(w => (Word: w, Now: now.GetValueOrDefault(w.Lemma), Then: then.GetValueOrDefault(w.Lemma)))
            .Where(c => c.Now + c.Then >= KeynessReport.MinOccurrences)
            .Select(c => (c.Word, c.Now, c.Then, Keyness: VocabularyView.Keyness(c.Now, c.Then, nowTotal, thenTotal)))
            .Where(c => Math.Abs(c.Keyness) >= KeynessReport.Threshold)];

        VocabTable Table(string title, bool rising) => new()
        {
            Title = title,
            Columns =
            [
                .. Vocab.WordColumns,
                Vocab.Column("Recent / 1000", VocabColumnKind.Decimal),
                Vocab.Column("Before / 1000", VocabColumnKind.Decimal),
                Vocab.Column("Recent", VocabColumnKind.Number),
                Vocab.Column("Before", VocabColumnKind.Number),
                Vocab.Column("Keyness", VocabColumnKind.Decimal)
            ],
            Rows = [.. changes.Where(c => rising ? c.Keyness > 0 : c.Keyness < 0)
                .OrderByDescending(c => Math.Abs(c.Keyness))
                .Take(KeynessReport.MaxRows)
                .Select(c => Vocab.Row(Vocab.WordCells(view, c.Word),
                    Vocab.Decimal(VocabularyView.PerThousand(c.Now, nowTotal), "0.00"),
                    Vocab.Decimal(VocabularyView.PerThousand(c.Then, thenTotal), "0.00"),
                    Vocab.Number(c.Now),
                    Vocab.Number(c.Then),
                    Vocab.Decimal(Math.Abs(c.Keyness))))],
            SortColumn = 9,
            Descending = true
        };

        VocabReportResult result = Result($"Recent: {recent.Count} call(s), {nowTotal} of your words. Before: {earlier.Count} call(s), {thenTotal} words.");
        result.Tables = [Table("Rising: you use these more now", true), Table("Falling: you use these less now", false)];
        return result;
    }

    private static Dictionary<string, int> Count(VocabularyView view, IEnumerable<CallTokens> calls)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (VocabToken token in calls.SelectMany(c => view.Words(c, true)))
        {
            counts[token.Token.Lemma] = counts.GetValueOrDefault(token.Token.Lemma) + 1;
        }
        return counts;
    }
}

internal class UptakeReport : VocabularyReport
{
    public override string Key => "uptake";
    public override string Title => "Picked up from partners";
    public override string Group => "Compare";
    public override string Description => "Words a partner said before you ever did, that you later used yourself.";
    public override int Order => 3;

    public override VocabOption[] Options =>
    [
        Choice("when", "Counts when you said it", ("later", "In a later call"), ("any", "Later in the same call, too"))
    ];

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        if (!view.HasPartners)
        {
            return Result(Vocab.NoPartnersNote);
        }
        bool sameCall = Option(options, "when") == "any";
        // Order over all of history, so "before you ever did" is not limited by the filter
        Dictionary<string, int> order = view.AllCalls.Select((c, i) => (c.CallId, i)).ToDictionary(x => x.CallId, x => x.i);
        Dictionary<string, (int Call, double Time)> firstSaidEver = [];
        foreach ((CallTokens call, int index) in view.AllCalls.Select((c, i) => (c, i)))
        {
            foreach (VocabToken token in view.Words(call, true))
            {
                firstSaidEver.TryAdd(token.Token.Lemma, (index, token.Time));
            }
        }

        List<(WordCount Word, CallTokens Heard, double HeardAt, CallTokens Said, double SaidAt)> picked = [];
        foreach (WordCount word in view.Listed.Where(w => w.Learner > 0 && w.FirstHeard != null && w.FirstSaid != null))
        {
            (CallTokens heard, double heardAt) = word.FirstHeard!.Value;
            (CallTokens said, double saidAt) = word.FirstSaid!.Value;
            if (firstSaidEver.TryGetValue(word.Lemma, out var ever) && ever.Call < order[said.CallId])
            {
                // Said before the filtered period began
                continue;
            }
            int heardIndex = order[heard.CallId];
            int saidIndex = order[said.CallId];
            bool before = heardIndex < saidIndex || (sameCall && heardIndex == saidIndex && heardAt < saidAt);
            if (before)
            {
                picked.Add((word, heard, heardAt, said, saidAt));
            }
        }

        VocabReportResult result = Result(sameCall
            ? "Within one call this includes words you simply echoed after your partner."
            : "Words you first used in a later call than the one you first heard them in.");
        result.Stats = [Vocab.Stat("Words picked up", picked.Count.ToString())];
        result.Tables =
        [
            new VocabTable
            {
                Title = "Picked up",
                Columns =
                [
                    .. Vocab.WordColumns,
                    Vocab.Column("First heard", VocabColumnKind.Call),
                    Vocab.Column("First said", VocabColumnKind.Call),
                    Vocab.Column("Your uses", VocabColumnKind.Number),
                    Vocab.Column("Their uses", VocabColumnKind.Number)
                ],
                Rows = [.. picked.Select(p => Vocab.Row(Vocab.WordCells(view, p.Word),
                    Vocab.Call(p.Heard, p.HeardAt),
                    Vocab.Call(p.Said, p.SaidAt),
                    Vocab.Number(p.Word.Learner),
                    Vocab.Number(p.Word.Partner)))],
                SortColumn = 6,
                Descending = true
            }
        ];
        return result;
    }
}

internal class PartnersReport : VocabularyReport
{
    public override string Key => "partners";
    public override string Title => "Per partner";
    public override string Group => "Compare";
    public override string Description => "Each partner's vocabulary, how much of it you share, and their words you never use.";
    public override int Order => 4;

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        if (!view.HasPartners)
        {
            return Result(Vocab.NoPartnersNote);
        }
        HashSet<string> everSaid = [.. view.AllCalls.SelectMany(c => view.Words(c, true)).Select(t => t.Token.Lemma)];
        List<VocabRow> rows = [];
        foreach (IGrouping<string, CallTokens> partner in view.Calls.Where(c => c.HasPartner).GroupBy(c => c.Partner.Length > 0 ? c.Partner : "(unnamed)"))
        {
            List<string> theirs = [.. partner.SelectMany(c => view.Words(c, false)).Select(t => t.Token.Lemma)];
            List<string> mine = [.. partner.SelectMany(c => view.Words(c, true)).Select(t => t.Token.Lemma)];
            HashSet<string> theirDistinct = [.. theirs];
            HashSet<string> myDistinct = [.. mine];
            int shared = theirDistinct.Count(myDistinct.Contains);
            string unused = string.Join("、", theirs
                .Where(l => !everSaid.Contains(l))
                .GroupBy(l => l)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key} {g.Count()}×"));
            double? mattr = VocabularyView.Mattr(theirs);
            rows.Add(Vocab.Row(
                Vocab.Text(partner.Key),
                Vocab.Number(partner.Count()),
                Vocab.Number(theirs.Count),
                Vocab.Number(theirDistinct.Count),
                Vocab.Number(myDistinct.Count),
                theirDistinct.Count == 0 ? Vocab.Text("-") : Vocab.Percent((double)shared / theirDistinct.Count),
                Vocab.Decimal(mattr * 100, "0"),
                Vocab.Text(unused)));
        }

        VocabReportResult result = Result();
        result.Tables =
        [
            new VocabTable
            {
                Title = "Partners",
                Columns =
                [
                    Vocab.Column("Partner", VocabColumnKind.Text),
                    Vocab.Column("Calls", VocabColumnKind.Number),
                    Vocab.Column("Their words", VocabColumnKind.Number),
                    Vocab.Column("Their different", VocabColumnKind.Number),
                    Vocab.Column("Your different", VocabColumnKind.Number, "Different words you used in calls with them."),
                    Vocab.Column("Shared", VocabColumnKind.Percent, "Share of their different words you also used with them."),
                    Vocab.Column("Their variety %", VocabColumnKind.Decimal),
                    Vocab.Column("Their words you never say", VocabColumnKind.Text)
                ],
                Rows = [.. rows],
                SortColumn = 1,
                Descending = true
            }
        ];
        return result;
    }
}
