using Akagi.Bridge.Chat.Models;

namespace Akagi.Life.Japanese.Vocabulary.Reports;

/// <summary>
/// Shared by the form reports: one side's rate next to the other's, with an example of the learner's.
/// </summary>
internal static class FormTable
{
    public static (VocabSeries Series, VocabTable Table) Build(VocabularyView view, string title, string unit, string seriesTitle,
        Dictionary<string, (int Learner, int Partner, string Example)> counts, int learnerBase, int partnerBase, IReadOnlyList<string>? order = null, int chartTop = 12)
    {
        double Rate(int count, int total) => total == 0 ? 0 : count * 100.0 / total;
        List<string> keys = order != null
            ? [.. order.Where(counts.ContainsKey)]
            : [.. counts.OrderByDescending(c => c.Value.Learner + c.Value.Partner).Select(c => c.Key)];

        VocabSeries series = new()
        {
            Title = seriesTitle,
            Names = view.HasPartners ? [Vocab.You, Vocab.Partners] : [Vocab.You],
            Kind = VocabSeriesKind.Columns,
            Unit = VocabUnit.Decimal,
            Hint = unit,
            Points = [.. keys.Take(chartTop).Select(k => new VocabPoint
            {
                Label = k,
                Values = view.HasPartners
                    ? [Math.Round(Rate(counts[k].Learner, learnerBase), 1), Math.Round(Rate(counts[k].Partner, partnerBase), 1)]
                    : [Math.Round(Rate(counts[k].Learner, learnerBase), 1)]
            })]
        };
        VocabTable table = new()
        {
            Title = title,
            Description = unit,
            Columns =
            [
                Vocab.Column("Form", VocabColumnKind.Text),
                Vocab.Column("You per 100", VocabColumnKind.Decimal),
                Vocab.Column("Partners per 100", VocabColumnKind.Decimal),
                Vocab.Column(Vocab.You, VocabColumnKind.Number),
                Vocab.Column(Vocab.Partners, VocabColumnKind.Number),
                Vocab.Column("Your example", VocabColumnKind.Text)
            ],
            Rows = [.. keys.Select(k => Vocab.Row(
                Vocab.Text(k),
                Vocab.Decimal(Rate(counts[k].Learner, learnerBase)),
                Vocab.Decimal(Rate(counts[k].Partner, partnerBase)),
                Vocab.Number(counts[k].Learner),
                Vocab.Number(counts[k].Partner),
                Vocab.Text(counts[k].Example)))],
            SortColumn = 1,
            Descending = true
        };
        return (series, table);
    }

    public static void Add(Dictionary<string, (int Learner, int Partner, string Example)> counts, string key, bool learner, string example)
    {
        (int l, int p, string e) = counts.GetValueOrDefault(key, (0, 0, string.Empty));
        counts[key] = learner ? (l + 1, p, e.Length == 0 ? example : e) : (l, p + 1, e);
    }
}

internal class VerbFormsReport : VocabularyReport
{
    public static readonly string[] FormOrder =
    [
        "dictionary form", "past (た)", "て-form", "ている (ongoing)", "negative (ない)", "polite (ます)", "want (たい)",
        "volitional (よう)", "potential / passive (られる)", "causative (させる)", "conditional (たら)", "conditional (ば)",
        "てしまう / ちゃう", "てみる", "ておく", "てくれる / てもらう / てあげる", "imperative"
    ];

    public override string Key => "verb-forms";
    public override string Title => "Verb forms";
    public override string Group => "Form";
    public override string Description => "Which verb forms you actually produce compared with your partners: a picture of your grammar range in real speech.";

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        Dictionary<string, (int Learner, int Partner, string Example)> counts = [];
        int learnerVerbs = 0;
        int partnerVerbs = 0;
        foreach (CallTokens call in view.Calls)
        {
            foreach (VocabSentence sentence in call.Sentences)
            {
                for (int i = sentence.Start; i < sentence.End; i++)
                {
                    DetailedToken verb = call.Tokens[i].Token;
                    if (verb.Pos != "動詞" || verb.Pos1 != "自立")
                    {
                        continue;
                    }
                    if (sentence.Learner)
                    {
                        learnerVerbs++;
                    }
                    else
                    {
                        partnerVerbs++;
                    }
                    foreach (string form in Classify(call.Tokens, i, sentence.End))
                    {
                        FormTable.Add(counts, form, sentence.Learner, call.Text(sentence));
                    }
                }
            }
        }

        (VocabSeries series, VocabTable table) = FormTable.Build(view, "Verb forms", "Per 100 verbs", "Verb forms per 100 verbs", counts, learnerVerbs, partnerVerbs, FormOrder, 17);
        VocabReportResult result = Result($"You used {learnerVerbs} verbs" + (view.HasPartners ? $", your partners {partnerVerbs}." : ".") +
            " A verb can show several forms at once, e.g. 食べていた is ongoing and past.");
        result.Series = [series];
        result.Tables = [table];
        return result;
    }

    /// <summary>
    /// The forms a verb takes from the auxiliaries and particles that follow it in the same sentence.
    /// </summary>
    internal static HashSet<string> Classify(IReadOnlyList<VocabToken> tokens, int index, int end)
    {
        HashSet<string> forms = [];
        DetailedToken verb = tokens[index].Token;
        if (verb.ConjugatedForm.StartsWith("命令", StringComparison.Ordinal))
        {
            forms.Add("imperative");
        }

        bool afterTe = false;
        for (int j = index + 1; j < end; j++)
        {
            DetailedToken next = tokens[j].Token;
            if (next.Pos == "助動詞")
            {
                switch (next.Lemma)
                {
                    case "た" or "だ" when next.Surface is "たら" or "だら":
                        forms.Add("conditional (たら)");
                        break;
                    case "た" or "だ":
                        forms.Add("past (た)");
                        break;
                    case "ない" or "ぬ" or "ん":
                        forms.Add("negative (ない)");
                        break;
                    case "ます":
                        forms.Add("polite (ます)");
                        break;
                    case "たい":
                        forms.Add("want (たい)");
                        break;
                    case "う" or "よう":
                        forms.Add("volitional (よう)");
                        break;
                }
                afterTe = false;
            }
            else if (next.Pos == "動詞" && next.Pos1 is "接尾" or "非自立")
            {
                switch (next.Lemma)
                {
                    case "れる" or "られる":
                        forms.Add("potential / passive (られる)");
                        break;
                    case "せる" or "させる":
                        forms.Add("causative (させる)");
                        break;
                    case "いる" or "てる" or "る" when afterTe:
                        forms.Add("ている (ongoing)");
                        break;
                    case "しまう" or "ちゃう" or "じゃう" or "ちまう":
                        forms.Add("てしまう / ちゃう");
                        break;
                    case "みる" when afterTe:
                        forms.Add("てみる");
                        break;
                    case "おく" or "とく" when afterTe:
                        forms.Add("ておく");
                        break;
                    case "くれる" or "もらう" or "あげる" or "くださる" or "いただく" when afterTe:
                        forms.Add("てくれる / てもらう / てあげる");
                        break;
                }
                afterTe = false;
            }
            else if (next.Pos == "助詞" && next.Pos1 == "接続助詞" && next.Lemma is "て" or "で")
            {
                afterTe = true;
                // A て followed by nothing that makes it ongoing, completed or the like is the plain て-form
                if (j + 1 >= end || tokens[j + 1].Token.Pos != "動詞" || tokens[j + 1].Token.Pos1 != "非自立")
                {
                    forms.Add("て-form");
                }
            }
            else if (next.Pos == "助詞" && next.Pos1 == "接続助詞" && next.Lemma == "ば")
            {
                forms.Add("conditional (ば)");
            }
            else
            {
                break;
            }
        }

        if (forms.Count == 0 && verb.ConjugatedForm == "基本形")
        {
            forms.Add("dictionary form");
        }
        return forms;
    }
}

internal class EndingsReport : VocabularyReport
{
    private static readonly HashSet<string> EndingParticleTypes = ["終助詞", "副助詞／並立助詞／終助詞"];
    private static readonly HashSet<string> TrailingConjunctions = ["けど", "けれど", "けれども", "し", "から", "が", "のに", "って"];

    public override string Key => "endings";
    public override string Title => "Sentence endings";
    public override string Group => "Form";
    public override string Description => "How you end your sentences (よ, ね, よね, かな, けど, です...) compared with your partners. A big part of sounding natural in casual Japanese.";
    public override int Order => 1;

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        Dictionary<string, (int Learner, int Partner, string Example)> counts = [];
        int learnerSentences = 0;
        int partnerSentences = 0;
        foreach (CallTokens call in view.Calls)
        {
            foreach (VocabSentence sentence in call.Sentences)
            {
                if (sentence.Learner)
                {
                    learnerSentences++;
                }
                else
                {
                    partnerSentences++;
                }
                FormTable.Add(counts, Ending(call.Tokens, sentence), sentence.Learner, call.Text(sentence));
            }
        }

        (VocabSeries series, VocabTable table) = FormTable.Build(view, "Sentence endings", "Per 100 sentences", "Endings per 100 sentences", counts, learnerSentences, partnerSentences);
        VocabReportResult result = Result("Each sentence is counted by its ending particles (よね, かな, けど...), else its last particle or auxiliary (で, です, た), else the kind of word it ends on.");
        result.Series = [series];
        result.Tables = [table];
        return result;
    }

    internal static string Ending(IReadOnlyList<VocabToken> tokens, VocabSentence sentence)
    {
        List<DetailedToken> words = [.. tokens.Skip(sentence.Start).Take(sentence.End - sentence.Start)
            .Select(t => t.Token)
            .Where(t => !t.IsPunctuation)];
        if (words.Count == 0)
        {
            return "(none)";
        }

        List<string> particles = [];
        for (int i = words.Count - 1; i >= 0 && particles.Count < 3; i--)
        {
            DetailedToken token = words[i];
            bool ending = token.Pos == "助詞" && (EndingParticleTypes.Contains(token.Pos1)
                || (token.Pos1 == "接続助詞" && TrailingConjunctions.Contains(token.Surface))
                || token.Surface is "の" or "か" && i == words.Count - 1);
            if (!ending)
            {
                break;
            }
            particles.Insert(0, token.Surface);
        }
        if (particles.Count > 0)
        {
            return "〜" + string.Concat(particles);
        }

        // Without an ending particle: the last particle or auxiliary itself (〜で, 〜って, 〜です), or what kind of word it is
        DetailedToken last = words[^1];
        if (last.Pos is "助詞" or "助動詞")
        {
            return "〜" + last.Surface;
        }
        return last.Pos switch
        {
            "名詞" => "noun",
            "動詞" => "verb",
            "形容詞" => "i-adjective",
            "副詞" => "adverb",
            "感動詞" or "フィラー" => "reaction only",
            _ => "other"
        };
    }
}

internal class ReactionsReport : VocabularyReport
{
    /// <summary>
    /// Adverbs and nouns that are mostly back-channel in speech.
    /// </summary>
    private static readonly HashSet<string> ReactionWords = ["そう", "まあ", "なるほど", "確か", "たしかに", "本当", "ほんと", "マジ", "まじ", "やっぱり", "やっぱ", "へえ", "へー"];

    public override string Key => "reactions";
    public override string Title => "Reactions and back-channel";
    public override string Group => "Form";
    public override string Description => "The うん, そう, へえ and なるほど of a conversation: which you lean on and which natives use.";
    public override int Order => 2;

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        Dictionary<string, (int Learner, int Partner, string Example)> counts = [];
        int learnerSentences = view.Calls.Sum(c => c.Sentences.Count(s => s.Learner));
        int partnerSentences = view.Calls.Sum(c => c.Sentences.Count(s => !s.Learner));
        foreach (CallTokens call in view.Calls)
        {
            foreach (VocabSentence sentence in call.Sentences)
            {
                for (int i = sentence.Start; i < sentence.End; i++)
                {
                    DetailedToken token = call.Tokens[i].Token;
                    if (token.Class == WordClass.Interjection || ReactionWords.Contains(token.Lemma))
                    {
                        FormTable.Add(counts, token.Lemma, sentence.Learner, call.Text(sentence));
                    }
                }
            }
        }

        (VocabSeries series, VocabTable table) = FormTable.Build(view, "Reactions", "Per 100 sentences of that side", "Reactions per 100 sentences", counts, learnerSentences, partnerSentences);
        VocabReportResult result = Result();
        result.Stats =
        [
            Vocab.Stat("Different reactions you use", counts.Count(c => c.Value.Learner > 0).ToString(),
                view.HasPartners ? $"your partners: {counts.Count(c => c.Value.Partner > 0)}" : string.Empty)
        ];
        result.Series = [series];
        result.Tables = [table];
        return result;
    }
}

internal class ChunksReport : VocabularyReport
{
    public const int MinCount = 3;
    public const int MaxLength = 4;
    public const int MaxRows = 300;

    public override string Key => "chunks";
    public override string Title => "Chunks and set phrases";
    public override string Group => "Chunks";
    public override string Description => "Word sequences of two to four words that come back again and again: your stock phrases and the natives' ones.";

    public override VocabOption[] Options =>
    [
        Choice("whose", "Show", ("yours", "Your most frequent"), ("theirs", "Your partners' most frequent"), ("yours-more", "Yours more than theirs"), ("theirs-more", "Theirs more than yours"))
    ];

    public override VocabReportResult Run(VocabularyView view, IReadOnlyDictionary<string, string> options)
    {
        string whose = Option(options, "whose");
        Dictionary<string, (int Learner, int Partner, string Example)> chunks = Count(view);
        int mine = Math.Max(1, view.SpokenTokens(true));
        int theirs = Math.Max(1, view.SpokenTokens(false));
        string search = view.Filter.Search.Trim();

        IEnumerable<(string Chunk, int Learner, int Partner, string Example, double Keyness)> rows = chunks
            .Where(c => search.Length == 0 || c.Key.Contains(search, StringComparison.Ordinal))
            .Select(c => (Chunk: c.Key, c.Value.Learner, c.Value.Partner, c.Value.Example, Keyness: VocabularyView.Keyness(c.Value.Learner, c.Value.Partner, mine, theirs)));
        rows = whose switch
        {
            "theirs" => rows.Where(r => r.Partner >= MinCount).OrderByDescending(r => r.Partner),
            "yours-more" => rows.Where(r => r.Learner >= MinCount && r.Keyness >= KeynessReport.Threshold).OrderByDescending(r => r.Keyness),
            "theirs-more" => rows.Where(r => r.Partner >= MinCount && r.Keyness <= -KeynessReport.Threshold).OrderBy(r => r.Keyness),
            _ => rows.Where(r => r.Learner >= MinCount).OrderByDescending(r => r.Learner)
        };
        if (whose is "theirs" or "theirs-more" or "yours-more" && !view.HasPartners)
        {
            return Result(Vocab.NoPartnersNote);
        }

        VocabReportResult result = Result("Sequences of words inside one sentence, without punctuation and fillers; shorter ones that almost only occur inside a longer one are left out.");
        result.Tables =
        [
            new VocabTable
            {
                Title = Options[0].Choices.First(c => c.Value == whose).Label,
                Columns =
                [
                    Vocab.Column("Chunk", VocabColumnKind.Text),
                    Vocab.Column(Vocab.You, VocabColumnKind.Number),
                    Vocab.Column(Vocab.Partners, VocabColumnKind.Number),
                    Vocab.Column("You / 1000", VocabColumnKind.Decimal),
                    Vocab.Column("Partners / 1000", VocabColumnKind.Decimal),
                    Vocab.Column("Example", VocabColumnKind.Text)
                ],
                Rows = [.. rows.Take(MaxRows).Select(r => Vocab.Row(
                    Vocab.Text(r.Chunk),
                    Vocab.Number(r.Learner),
                    Vocab.Number(r.Partner),
                    Vocab.Decimal(VocabularyView.PerThousand(r.Learner, mine), "0.00"),
                    Vocab.Decimal(VocabularyView.PerThousand(r.Partner, theirs), "0.00"),
                    Vocab.Text(r.Example)))],
                SortColumn = whose is "theirs" or "theirs-more" ? 2 : 1,
                Descending = true
            }
        ];
        return result;
    }

    internal static Dictionary<string, (int Learner, int Partner, string Example)> Count(VocabularyView view)
    {
        Dictionary<string, (int Learner, int Partner, string Example)> counts = new(StringComparer.Ordinal);
        foreach (CallTokens call in view.Calls)
        {
            foreach (VocabSentence sentence in call.Sentences)
            {
                List<DetailedToken> words = [.. call.Tokens.Skip(sentence.Start).Take(sentence.End - sentence.Start)
                    .Select(t => t.Token)
                    .Where(t => !t.IsPunctuation && !t.IsFiller)];
                for (int length = 2; length <= MaxLength; length++)
                {
                    for (int start = 0; start + length <= words.Count; start++)
                    {
                        List<DetailedToken> run = words.GetRange(start, length);
                        // At least one word that carries meaning, and not only reactions
                        if (!run.Any(t => t.Class is WordClass known && known != WordClass.Interjection))
                        {
                            continue;
                        }
                        FormTable.Add(counts, string.Concat(run.Select(t => t.Surface)), sentence.Learner, call.Text(sentence));
                    }
                }
            }
        }

        // A short chunk that almost only occurs inside a longer one is that longer chunk
        List<KeyValuePair<string, (int Learner, int Partner, string Example)>> frequent = [.. counts.Where(c => c.Value.Learner + c.Value.Partner >= MinCount)];
        Dictionary<string, (int Learner, int Partner, string Example)> kept = new(StringComparer.Ordinal);
        foreach ((string chunk, var value) in frequent)
        {
            int total = value.Learner + value.Partner;
            bool covered = frequent.Any(other => other.Key.Length > chunk.Length && other.Key.Contains(chunk, StringComparison.Ordinal)
                                                 && other.Value.Learner + other.Value.Partner >= total * 0.8);
            if (!covered)
            {
                kept[chunk] = value;
            }
        }
        return kept;
    }
}
