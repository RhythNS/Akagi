using Akagi.Bridge.Chat.Models;

namespace Akagi.Life.Japanese.Vocabulary;

/// <summary>
/// One word over the calls in view: how often each side said it and when first.
/// </summary>
internal class WordCount
{
    public required string Lemma { get; init; }
    public required string Reading { get; init; }
    public required WordClass Class { get; init; }
    public int Learner { get; set; }
    public int Partner { get; set; }
    /// <summary>
    /// Calls the learner used it in.
    /// </summary>
    public int LearnerCalls { get; set; }
    public int PartnerCalls { get; set; }
    public (CallTokens Call, double Time)? FirstSaid { get; set; }
    public (CallTokens Call, double Time)? FirstHeard { get; set; }
    public Dictionary<string, (int Learner, int Partner)> Forms { get; } = [];
    public int Total => Learner + Partner;
}

/// <summary>
/// The calls and words the page's filter selects, with the counts and measures every report builds on.
/// </summary>
internal class VocabularyView
{
    public const int MattrWindow = 50;
    private const int MaxSearchMeaning = 200;

    private static readonly HashSet<WordClass> DefaultClasses = [.. JapaneseTokenizer.ContentClasses];

    private readonly HashSet<WordClass> _classes;
    private Dictionary<string, WordCount>? _words;

    public VocabularyView(IReadOnlyList<CallTokens> allCalls, VocabFilter filter, IWordDictionary dictionary)
    {
        Filter = filter;
        Dictionary = dictionary;
        AllCalls = allCalls;
        Calls = [.. allCalls.Where(c =>
            (filter.Partner.Length == 0 || string.Equals(c.Partner, filter.Partner.Trim(), StringComparison.OrdinalIgnoreCase))
            && (filter.From == null || c.Date.Date >= filter.From.Value.Date)
            && (filter.To == null || c.Date.Date <= filter.To.Value.Date))];
        _classes = filter.Classes.Length == 0
            ? DefaultClasses
            : [.. filter.Classes.Select(c => Enum.TryParse(c, out WordClass parsed) ? parsed : (WordClass?)null).OfType<WordClass>()];
    }

    public VocabFilter Filter { get; }
    public IWordDictionary Dictionary { get; }
    /// <summary>
    /// Every call, regardless of the filter, oldest first; for "new" and "first heard" across all history.
    /// </summary>
    public IReadOnlyList<CallTokens> AllCalls { get; }
    /// <summary>
    /// The calls the filter selects, oldest first.
    /// </summary>
    public List<CallTokens> Calls { get; }

    public bool HasPartners => Calls.Any(c => c.HasPartner);

    /// <summary>
    /// A token counts as a word: the right class, not a name unless asked for, and Japanese.
    /// </summary>
    public bool IsWord(DetailedToken token) =>
        token.Class is WordClass wordClass && _classes.Contains(wordClass)
        && (Filter.IncludeNames || !token.IsProperNoun)
        && token.Lemma.Any(JapaneseText.IsJapanese);

    public static IEnumerable<VocabToken> Side(CallTokens call, bool learner) => call.Tokens.Where(t => t.Learner == learner);

    public IEnumerable<VocabToken> Words(CallTokens call, bool learner) => Side(call, learner).Where(t => IsWord(t.Token));

    public int TotalWords(bool learner) => Calls.Sum(c => Words(c, learner).Count());

    /// <summary>
    /// Every word in view with its counts, before the minimum count and search are applied.
    /// </summary>
    public IReadOnlyDictionary<string, WordCount> Counts => _words ??= Count();

    /// <summary>
    /// The words a list shows: said at least <see cref="VocabFilter.MinCount"/> times and matching the search.
    /// </summary>
    public IEnumerable<WordCount> Listed => Counts.Values.Where(w => w.Total >= Math.Max(1, Filter.MinCount) && MatchesSearch(w));

    private bool MatchesSearch(WordCount word)
    {
        string search = Filter.Search.Trim();
        if (search.Length == 0)
        {
            return true;
        }
        if (word.Lemma.Contains(search, StringComparison.Ordinal) || word.Reading.Contains(JapaneseTokenizer.ToHiragana(search), StringComparison.Ordinal)
            || word.Forms.Keys.Any(f => f.Contains(search, StringComparison.Ordinal)))
        {
            return true;
        }
        string meaning = Info(word)?.Meaning ?? string.Empty;
        return meaning.Length <= MaxSearchMeaning && meaning.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, WordCount> Count()
    {
        Dictionary<string, WordCount> words = new(StringComparer.Ordinal);
        foreach (CallTokens call in Calls)
        {
            HashSet<string> saidHere = [];
            HashSet<string> heardHere = [];
            foreach (VocabToken token in call.Tokens.Where(t => IsWord(t.Token)))
            {
                string lemma = token.Token.Lemma;
                if (!words.TryGetValue(lemma, out WordCount? word))
                {
                    word = new WordCount { Lemma = lemma, Reading = JapaneseTokenizer.ReadingOf(lemma), Class = token.Token.Class!.Value };
                    words[lemma] = word;
                }
                (int learner, int partner) = word.Forms.GetValueOrDefault(token.Token.Surface);
                if (token.Learner)
                {
                    word.Learner++;
                    word.FirstSaid ??= (call, token.Time);
                    if (saidHere.Add(lemma))
                    {
                        word.LearnerCalls++;
                    }
                    word.Forms[token.Token.Surface] = (learner + 1, partner);
                }
                else
                {
                    word.Partner++;
                    word.FirstHeard ??= (call, token.Time);
                    if (heardHere.Add(lemma))
                    {
                        word.PartnerCalls++;
                    }
                    word.Forms[token.Token.Surface] = (learner, partner + 1);
                }
            }
        }
        return words;
    }

    public WordInfo? Info(WordCount word) => Dictionary.Lookup(word.Lemma, word.Reading, word.Class);

    /// <summary>
    /// Every token of one side that is said rather than punctuation or a filler, the base for rates of chunks and forms.
    /// </summary>
    public int SpokenTokens(bool learner) =>
        Calls.Sum(c => Side(c, learner).Count(t => !t.Token.IsPunctuation && !t.Token.IsFiller));

    /// <summary>
    /// Occurrences per thousand words of that side.
    /// </summary>
    public static double PerThousand(int count, int total) => total == 0 ? 0 : count * 1000.0 / total;

    /// <summary>
    /// Dunning's log-likelihood: how surprising the difference between a count in A and in B is, given their sizes.
    /// Positive when the word is more frequent in A, negative when in B. Above 3.84 is significant at p &lt; 0.05,
    /// above 10.83 at p &lt; 0.001.
    /// </summary>
    public static double Keyness(int a, int b, int totalA, int totalB)
    {
        if (totalA == 0 || totalB == 0 || a + b == 0)
        {
            return 0;
        }
        double expectedA = totalA * (double)(a + b) / (totalA + totalB);
        double expectedB = totalB * (double)(a + b) / (totalA + totalB);
        double g2 = 2 * ((a > 0 ? a * Math.Log(a / expectedA) : 0) + (b > 0 ? b * Math.Log(b / expectedB) : 0));
        return (double)a / totalA >= (double)b / totalB ? g2 : -g2;
    }

    /// <summary>
    /// How many times more frequent in A than in B, with half a count added to both so a zero does not divide.
    /// </summary>
    public static double Ratio(int a, int b, int totalA, int totalB) =>
        totalA == 0 || totalB == 0 ? 0 : ((a + 0.5) / totalA) / ((b + 0.5) / totalB);

    /// <summary>
    /// Moving-average type-token ratio: the share of different words in every window of <see cref="MattrWindow"/>
    /// words, averaged. Unlike the plain ratio it does not fall just because a call was longer.
    /// </summary>
    public static double? Mattr(IReadOnlyList<string> lemmas, int window = MattrWindow)
    {
        if (lemmas.Count == 0)
        {
            return null;
        }
        if (lemmas.Count <= window)
        {
            return (double)lemmas.Distinct().Count() / lemmas.Count;
        }
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        for (int i = 0; i < window; i++)
        {
            counts[lemmas[i]] = counts.GetValueOrDefault(lemmas[i]) + 1;
        }
        double sum = counts.Count;
        for (int i = window; i < lemmas.Count; i++)
        {
            string leaving = lemmas[i - window];
            if (--counts[leaving] == 0)
            {
                counts.Remove(leaving);
            }
            counts[lemmas[i]] = counts.GetValueOrDefault(lemmas[i]) + 1;
            sum += counts.Count;
        }
        return sum / (lemmas.Count - window + 1) / window;
    }

    public double? Mattr(CallTokens call, bool learner) => Mattr([.. Words(call, learner).Select(t => t.Token.Lemma)]);
}
