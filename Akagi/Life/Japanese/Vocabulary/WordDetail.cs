using Akagi.Bridge.Chat.Models;

namespace Akagi.Life.Japanese.Vocabulary;

/// <summary>
/// Everything about one word in the calls in view: its forms, who said it when, and every line it is in.
/// </summary>
internal static class WordDetail
{
    public const int MaxLines = 300;

    public static VocabWordDetail? Build(VocabularyView view, string lemma)
    {
        List<(CallTokens Call, VocabToken Token)> uses = [.. view.Calls
            .SelectMany(c => c.Tokens.Where(t => t.Token.Lemma == lemma).Select(t => (Call: c, Token: t)))];
        if (uses.Count == 0)
        {
            return null;
        }

        DetailedToken first = uses[0].Token.Token;
        string reading = JapaneseTokenizer.ReadingOf(lemma);
        WordInfo? info = view.Dictionary.Lookup(lemma, reading, first.Class);

        // Every sentence with the word once, newest call first, in call order within a call
        List<VocabLine> lines = [];
        foreach (CallTokens call in view.Calls.AsEnumerable().Reverse())
        {
            foreach (VocabSentence sentence in call.Sentences)
            {
                List<string> marks = [.. call.Tokens.Skip(sentence.Start).Take(sentence.End - sentence.Start)
                    .Where(t => t.Token.Lemma == lemma)
                    .Select(t => t.Token.Surface)
                    .Distinct()];
                if (marks.Count == 0)
                {
                    continue;
                }
                lines.Add(new VocabLine
                {
                    CallId = call.CallId,
                    CallName = call.Name,
                    Date = call.Date,
                    Time = sentence.Time,
                    IsLearner = sentence.Learner,
                    Speaker = sentence.Learner ? "You" : (call.Partner.Length > 0 ? call.Partner : "Partner"),
                    Text = call.Text(sentence),
                    Marks = [.. marks]
                });
            }
        }

        return new VocabWordDetail
        {
            Lemma = lemma,
            Reading = reading == lemma ? string.Empty : reading,
            Class = first.Class is WordClass wordClass ? Vocab.ClassLabel(wordClass) : first.Pos,
            Meaning = info?.Meaning ?? string.Empty,
            Band = info?.Band ?? string.Empty,
            Learner = uses.Count(u => u.Token.Learner),
            Partner = uses.Count(u => !u.Token.Learner),
            Forms = [.. uses.GroupBy(u => u.Token.Token.Surface)
                .Select(g => new VocabForm { Surface = g.Key, Learner = g.Count(u => u.Token.Learner), Partner = g.Count(u => !u.Token.Learner) })
                .OrderByDescending(f => f.Learner + f.Partner)],
            Timeline = new VocabSeries
            {
                Title = "Uses per call",
                Names = view.HasPartners ? [Vocab.You, Vocab.Partners] : [Vocab.You],
                Kind = VocabSeriesKind.Columns,
                Unit = VocabUnit.Count,
                Points = [.. view.Calls.Select(c => new VocabPoint
                {
                    Label = $"{c.Name}, {c.Date:yyyy-MM-dd}",
                    CallId = c.CallId,
                    Values = view.HasPartners
                        ? [uses.Count(u => u.Call == c && u.Token.Learner), c.HasPartner ? uses.Count(u => u.Call == c && !u.Token.Learner) : null]
                        : [uses.Count(u => u.Call == c && u.Token.Learner)]
                })]
            },
            Lines = [.. lines.Take(MaxLines)],
            TotalLines = lines.Count,
            FirstSaid = uses.FirstOrDefault(u => u.Token.Learner).Call?.Date,
            FirstHeard = uses.FirstOrDefault(u => !u.Token.Learner).Call?.Date
        };
    }
}
