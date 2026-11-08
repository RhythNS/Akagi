using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Japanese;
using Akagi.Connectors.Desu;
using Akagi.Life.Japanese.Vocabulary;
using Wacton.Desu.Japanese;

namespace Akagi.Life.Japanese.Conjugations;

/// <summary>
/// A verb from the dictionary with what the practice needs: how it conjugates, what it means, how common it is.
/// </summary>
internal sealed record DictionaryVerb(string Written, string Reading, VerbClass Class, string Meaning, int Rank)
{
    public string Id => ConjugationVerbView.MakeId(Written, Reading);
}

internal interface IVerbCatalog
{
    /// <summary>
    /// The dictionary is loaded; it parses in the background for a few seconds after the bot starts.
    /// </summary>
    public bool Ready { get; }

    /// <summary>
    /// Every verb with a frequency mark, most common first. Empty while the dictionary loads.
    /// </summary>
    public IReadOnlyList<DictionaryVerb> Common { get; }

    /// <summary>
    /// The most common verb written or read as the given dictionary form, or null.
    /// </summary>
    public DictionaryVerb? Find(string lemma);
}

/// <summary>
/// The verbs in JMdict whose conjugation is regular enough to practise, indexed once by written form and reading.
/// Verbs with archaic or rare classes (zuru, nidan, the 問う class) are left out.
/// </summary>
internal class VerbCatalog : IVerbCatalog
{
    private const int MaxGlosses = 3;

    private readonly Func<IReadOnlyList<IJapaneseEntry>?> _entries;
    private readonly object _gate = new();
    private (List<DictionaryVerb> Common, Dictionary<string, DictionaryVerb> Index)? _built;

    public VerbCatalog() : this(() => DesuConnector.LoadedEntries)
    {
    }

    internal VerbCatalog(Func<IReadOnlyList<IJapaneseEntry>?> entries)
    {
        _entries = entries;
    }

    public bool Ready => Built() != null;

    public IReadOnlyList<DictionaryVerb> Common => Built()?.Common ?? [];

    public DictionaryVerb? Find(string lemma)
    {
        if (Built() is not { } built)
        {
            return null;
        }
        Dictionary<string, DictionaryVerb> index = built.Index;
        return index.GetValueOrDefault(lemma) ?? index.GetValueOrDefault(Kana.ToHiragana(lemma));
    }

    /// <summary>
    /// The class a JMdict part of speech (as Desu names it) stands for, or null for anything not practised.
    /// </summary>
    internal static VerbClass? ClassOf(string partOfSpeech) => partOfSpeech switch
    {
        "Verb (ichidan)" => VerbClass.Ichidan,
        "Verb (ichidan, kureru special)" => VerbClass.IchidanKureru,
        "Verb (godan, iku/yuku special)" => VerbClass.GodanIku,
        "Verb (godan, aru special)" => VerbClass.GodanHonorific,
        "Verb (godan, ru, irregular)" => VerbClass.GodanAru,
        "Verb (kuru special)" => VerbClass.Kuru,
        "Verb (suru, included)" => VerbClass.Suru,
        "Verb (godan, u)" or "Verb (godan, ku)" or "Verb (godan, gu)" or "Verb (godan, su)" or "Verb (godan, tsu)" or "Verb (godan, nu)"
            or "Verb (godan, bu)" or "Verb (godan, mu)" or "Verb (godan, ru)" => VerbClass.Godan,
        _ => null
    };

    private (List<DictionaryVerb> Common, Dictionary<string, DictionaryVerb> Index)? Built()
    {
        if (_built != null)
        {
            return _built;
        }
        IReadOnlyList<IJapaneseEntry>? entries = _entries();
        if (entries == null || entries.Count == 0)
        {
            return null;
        }
        lock (_gate)
        {
            _built ??= Build(entries);
            return _built;
        }
    }

    private static (List<DictionaryVerb>, Dictionary<string, DictionaryVerb>) Build(IReadOnlyList<IJapaneseEntry> entries)
    {
        List<DictionaryVerb> all = [];
        Dictionary<string, DictionaryVerb> index = new(StringComparer.Ordinal);
        foreach (IJapaneseEntry entry in entries)
        {
            if (Read(entry) is not DictionaryVerb verb)
            {
                continue;
            }
            all.Add(verb);
            foreach (string key in entry.Kanjis.Select(k => k.Text).Concat(entry.Readings.Select(r => Kana.ToHiragana(r.Text))).Append(verb.Written).Distinct())
            {
                // A verb written exactly like the key wins over one only read like it: くれる is 呉れる, not 暮れる
                if (!index.TryGetValue(key, out DictionaryVerb? known) || Better(verb, known, key))
                {
                    index[key] = verb;
                }
            }
        }
        List<DictionaryVerb> common = [.. all.Where(v => v.Rank <= WordInfo.LessCommon).DistinctBy(v => v.Id).OrderBy(v => v.Rank).ThenBy(v => v.Reading, StringComparer.Ordinal)];
        return (common, index);
    }

    private static bool Better(DictionaryVerb candidate, DictionaryVerb known, string key)
    {
        bool candidateExact = candidate.Written == key;
        bool knownExact = known.Written == key;
        return candidateExact != knownExact ? candidateExact : candidate.Rank < known.Rank;
    }

    private static DictionaryVerb? Read(IJapaneseEntry entry)
    {
        VerbClass? verbClass = null;
        List<string> glosses = [];
        bool usuallyKana = false;
        foreach (ISense sense in entry.Senses)
        {
            VerbClass? senseClass = sense.PartsOfSpeech.Select(p => ClassOf(p.ToString() ?? string.Empty)).FirstOrDefault(c => c != null);
            if (senseClass == null)
            {
                continue;
            }
            if (verbClass == null)
            {
                // The main sense decides the spelling: 来る is written in kana only as an auxiliary
                verbClass = senseClass;
                usuallyKana = sense.Miscellanea.Any(m => m.ToString() == "UsuallyKanaAlone");
            }
            if (senseClass != verbClass)
            {
                continue;
            }
            glosses.AddRange(sense.Glosses.Where(g => g.Language.ThreeLetterCode == "eng").Select(g => g.Term));
        }
        IReading? reading = entry.Readings.FirstOrDefault();
        if (verbClass is not VerbClass found || reading == null)
        {
            return null;
        }
        string kana = Kana.ToHiragana(reading.Text);
        IKanji? kanji = entry.Kanjis.FirstOrDefault();
        string written = kanji == null || usuallyKana ? kana : kanji.Text;
        Verb verb = new(written, kana, found);
        // Entries whose spelling the rules cannot follow (no okurigana, odd endings) are not asked
        if (Conjugator.Conjugate(verb, ConjugationForm.Past) == null)
        {
            if (written == kana || Conjugator.Conjugate(verb with { Written = kana }, ConjugationForm.Past) == null)
            {
                return null;
            }
            written = kana;
        }
        int rank = Math.Min(
            JmdictWordDictionary.Rank(reading.Priorities.Select(p => p.ToString() ?? string.Empty)),
            kanji == null ? int.MaxValue : JmdictWordDictionary.Rank(kanji.Priorities.Select(p => p.ToString() ?? string.Empty)));
        return new DictionaryVerb(written, kana, found, string.Join("; ", glosses.Distinct().Take(MaxGlosses)), rank);
    }
}
