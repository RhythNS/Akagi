using NMeCab.Specialized;
using System.Collections.Concurrent;

namespace Akagi.Life.Japanese;

/// <summary>
/// The kinds of words counted as vocabulary. Particles, auxiliaries, fillers and symbols are grammar, not vocabulary.
/// </summary>
internal enum WordClass
{
    Noun,
    Verb,
    Adjective,
    /// <summary>A na-adjective stem such as 静か or 大丈夫.</summary>
    AdjectivalNoun,
    Adverb,
    /// <summary>この, そんな, 大きな...</summary>
    Adnominal,
    Conjunction,
    /// <summary>Interjections and back-channel words: うん, そう, へえ.</summary>
    Interjection
}

internal readonly record struct JapaneseToken(string Surface, string Lemma, string Reading, WordClass? Class);

/// <summary>
/// Splits Japanese into words with MeCab (NMeCab with the IPA dictionary): dictionary form, reading and word class.
/// The dictionary is about 50 MB and loads once, on first use.
/// </summary>
internal static class JapaneseTokenizer
{
    /// <summary>
    /// Words too common to tell anything about someone's vocabulary.
    /// </summary>
    private static readonly HashSet<string> LightWords = ["する", "ある", "いる", "なる", "れる", "られる", "せる", "させる", "くる", "いく", "おる", "ない"];

    /// <summary>
    /// Spoken forms the dictionary misreads: a surprised えっ comes out as the verb える.
    /// </summary>
    private static readonly HashSet<string> Exclamations = ["えっ", "えー", "ええっ", "あっ", "おっ", "はっ"];

    /// <summary>
    /// The word classes that carry meaning, as opposed to back-channel words and grammar-like adnominals.
    /// </summary>
    public static readonly WordClass[] ContentClasses = [WordClass.Noun, WordClass.Verb, WordClass.Adjective, WordClass.AdjectivalNoun, WordClass.Adverb];

    private static readonly Lazy<MeCabIpaDicTagger> Tagger = new(() => MeCabIpaDicTagger.Create(), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, string> LemmaReadings = new();

    public static List<JapaneseToken> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        MeCabIpaDicNode[] nodes;
        // The tagger reuses internal buffers, so one parse at a time
        lock (Gate)
        {
            nodes = Tagger.Value.Parse(text);
        }

        List<JapaneseToken> tokens = new(nodes.Length);
        foreach (MeCabIpaDicNode node in nodes)
        {
            if (string.IsNullOrEmpty(node.Surface))
            {
                continue;
            }
            string reading = string.IsNullOrEmpty(node.Reading) || node.Reading == "*" ? string.Empty : node.Reading;
            if (Exclamations.Contains(node.Surface))
            {
                tokens.Add(new JapaneseToken(node.Surface, node.Surface, reading, WordClass.Interjection));
                continue;
            }
            string lemma = string.IsNullOrEmpty(node.OriginalForm) || node.OriginalForm == "*" ? node.Surface : node.OriginalForm;
            tokens.Add(new JapaneseToken(node.Surface, lemma, reading, Classify(node.PartsOfSpeech, node.PartsOfSpeechSection1, lemma)));
        }
        return tokens;
    }

    /// <summary>
    /// The reading of a dictionary form, in hiragana. Readings MeCab reports belong to the surface (忙しく), so the
    /// dictionary form is read on its own.
    /// </summary>
    public static string ReadingOf(string lemma) => LemmaReadings.GetOrAdd(lemma, static l =>
    {
        List<JapaneseToken> tokens = Tokenize(l);
        string reading = string.Concat(tokens.Select(t => t.Reading.Length > 0 ? t.Reading : t.Surface));
        return ToHiragana(reading);
    });

    public static string ToHiragana(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c;
            }
        });

    internal static WordClass? Classify(string partOfSpeech, string section, string lemma)
    {
        if (LightWords.Contains(lemma))
        {
            return null;
        }
        return partOfSpeech switch
        {
            "名詞" => section switch
            {
                "形容動詞語幹" => WordClass.AdjectivalNoun,
                // こと, の, ん, それ, 3, さん: grammar, pronouns, numbers and suffixes
                "非自立" or "代名詞" or "数" or "接尾" or "特殊" => null,
                _ => WordClass.Noun
            },
            "動詞" => section == "自立" ? WordClass.Verb : null,
            "形容詞" => section == "自立" ? WordClass.Adjective : null,
            "副詞" => WordClass.Adverb,
            "連体詞" => WordClass.Adnominal,
            "接続詞" => WordClass.Conjunction,
            "感動詞" => WordClass.Interjection,
            _ => null
        };
    }
}
