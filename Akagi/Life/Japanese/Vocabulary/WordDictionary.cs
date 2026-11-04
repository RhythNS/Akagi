using Akagi.Connectors.Desu;
using Wacton.Desu.Japanese;

namespace Akagi.Life.Japanese.Vocabulary;

/// <summary>
/// What the dictionary knows about a word: its meaning and roughly how common it is.
/// </summary>
/// <param name="Rank">Approximate frequency rank in newspaper text; <see cref="int.MaxValue"/> when JMdict marks it as uncommon.</param>
internal record WordInfo(string Meaning, int Rank)
{
    public const int Common = 12_000;
    public const int LessCommon = 24_000;

    /// <summary>
    /// The frequency band in words, from JMdict's newspaper based priorities (nf01 to nf48, news, ichi, spec, gai).
    /// </summary>
    public string Band => Rank switch
    {
        <= 1_000 => "top 1k",
        <= 5_000 => "top 5k",
        <= 10_000 => "top 10k",
        Common => "common",
        <= LessCommon => "top 24k",
        _ => "uncommon"
    };
}

internal interface IWordDictionary
{
    /// <summary>
    /// The dictionary is loaded; it parses in the background for a few seconds after the bot starts.
    /// </summary>
    public bool Ready { get; }

    /// <summary>
    /// The entry for a dictionary form that best fits how it was used: its word class, its reading (hiragana), and for a
    /// kana word an entry usually written in kana. Null when the word is not in the dictionary or it is not loaded yet.
    /// </summary>
    public WordInfo? Lookup(string lemma, string reading, WordClass? wordClass = null);
}

/// <summary>
/// JMdict through the entries the Desu connector loads at startup, indexed once by written form and reading.
/// </summary>
internal class JmdictWordDictionary : IWordDictionary
{
    private const int MaxGlosses = 3;

    private readonly Func<IReadOnlyList<IJapaneseEntry>?> _entries;
    private readonly object _gate = new();
    private Dictionary<string, List<Candidate>>? _index;

    private sealed record Sense(string[] PartsOfSpeech, string[] Glosses);

    private sealed record Candidate(int Rank, HashSet<string> Readings, IReadOnlyList<Sense> Senses, bool KanaWord, bool Uncommon);

    public JmdictWordDictionary() : this(() => DesuConnector.LoadedEntries)
    {
    }

    internal JmdictWordDictionary(Func<IReadOnlyList<IJapaneseEntry>?> entries)
    {
        _entries = entries;
    }

    public bool Ready => Index() != null;

    public WordInfo? Lookup(string lemma, string reading, WordClass? wordClass = null)
    {
        if (Index() is not Dictionary<string, List<Candidate>> index || !index.TryGetValue(lemma, out List<Candidate>? candidates))
        {
            return null;
        }
        bool kanaLemma = !lemma.Any(c => c is >= '一' and <= '鿿');
        string[] wanted = wordClass is WordClass known ? JmdictPartsOfSpeech(known) : [];

        Candidate best = candidates
            .OrderByDescending(c => Score(c, reading, kanaLemma, wanted))
            .ThenBy(c => c.Rank)
            .First();

        // The meaning of the senses used as this kind of word: あまり as an adverb is "not very", not "remainder"
        List<Sense> fitting = [.. best.Senses.Where(s => Fits(s, wanted))];
        IEnumerable<string> glosses = (fitting.Count > 0 ? fitting : best.Senses).SelectMany(s => s.Glosses);
        return new WordInfo(string.Join("; ", glosses.Distinct().Take(MaxGlosses)), best.Rank);
    }

    private static int Score(Candidate candidate, string reading, bool kanaLemma, string[] wanted)
    {
        int score = 0;
        if (wanted.Length > 0 && candidate.Senses.Any(s => Fits(s, wanted)))
        {
            score += 4;
        }
        if (kanaLemma && candidate.KanaWord)
        {
            score += 2;
        }
        if (reading.Length > 0 && candidate.Readings.Contains(reading))
        {
            score += 1;
        }
        if (candidate.Uncommon)
        {
            score -= 2;
        }
        return score;
    }

    private static bool Fits(Sense sense, string[] wanted) =>
        wanted.Length > 0 && sense.PartsOfSpeech.Any(p => wanted.Any(w => p.StartsWith(w, StringComparison.Ordinal)));

    /// <summary>
    /// How JMdict (through Desu) names the parts of speech of a word class.
    /// </summary>
    internal static string[] JmdictPartsOfSpeech(WordClass wordClass) => wordClass switch
    {
        WordClass.Noun => ["Noun"],
        WordClass.Verb => ["Verb", "Ichidan", "Godan", "Suru"],
        WordClass.Adjective => ["Adjective (i", "Adjective (keiyoushi"],
        WordClass.AdjectivalNoun => ["Adjective (na", "Noun"],
        WordClass.Adverb => ["Adverb"],
        WordClass.Adnominal => ["Pre-noun", "Adjective (pre"],
        WordClass.Conjunction => ["Conjunction"],
        _ => ["Interjection"]
    };

    private Dictionary<string, List<Candidate>>? Index()
    {
        if (_index != null)
        {
            return _index;
        }
        IReadOnlyList<IJapaneseEntry>? entries = _entries();
        if (entries == null || entries.Count == 0)
        {
            return null;
        }
        lock (_gate)
        {
            _index ??= Build(entries);
            return _index;
        }
    }

    private static Dictionary<string, List<Candidate>> Build(IReadOnlyList<IJapaneseEntry> entries)
    {
        Dictionary<string, List<Candidate>> index = new(StringComparer.Ordinal);
        foreach (IJapaneseEntry entry in entries)
        {
            List<Sense> senses = [.. entry.Senses.Select(s => new Sense(
                [.. s.PartsOfSpeech.Select(p => p.ToString() ?? string.Empty)],
                [.. s.Glosses.Where(g => g.Language.ThreeLetterCode == "eng").Select(g => g.Term)]))];
            HashSet<string> misc = [.. entry.Senses.SelectMany(s => s.Miscellanea).Select(m => m.ToString() ?? string.Empty)];
            bool kanaWord = !entry.Kanjis.Any() || misc.Contains("UsuallyKanaAlone");
            bool uncommon = misc.Contains("Archaic") || misc.Contains("Rare") || misc.Contains("Obsolete");
            HashSet<string> readings = [.. entry.Readings.Select(r => JapaneseTokenizer.ToHiragana(r.Text))];

            foreach (IKanji kanji in entry.Kanjis)
            {
                Add(index, kanji.Text, new Candidate(Rank(kanji.Priorities.Select(p => p.ToString() ?? string.Empty)), readings, senses, kanaWord, uncommon));
            }
            foreach (IReading reading in entry.Readings)
            {
                Add(index, reading.Text, new Candidate(Rank(reading.Priorities.Select(p => p.ToString() ?? string.Empty)), readings, senses, kanaWord, uncommon));
            }
        }
        return index;
    }

    private static void Add(Dictionary<string, List<Candidate>> index, string key, Candidate candidate)
    {
        if (!index.TryGetValue(key, out List<Candidate>? list))
        {
            list = [];
            index[key] = list;
        }
        list.Add(candidate);
    }

    /// <summary>
    /// nfXX ranks a word among the XX-th 500 most frequent in newspapers; the news, ichi, spec and gai marks without one
    /// mean common (1) or less common (2).
    /// </summary>
    internal static int Rank(IEnumerable<string> priorities)
    {
        int rank = int.MaxValue;
        foreach (string priority in priorities)
        {
            if (priority.StartsWith("Frequency", StringComparison.Ordinal) && int.TryParse(priority["Frequency".Length..], out int band))
            {
                rank = Math.Min(rank, band * 500);
            }
            else if (priority.EndsWith('1'))
            {
                rank = Math.Min(rank, WordInfo.Common);
            }
            else if (priority.EndsWith('2'))
            {
                rank = Math.Min(rank, WordInfo.LessCommon);
            }
        }
        return rank;
    }
}
