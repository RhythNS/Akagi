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
/// A token with MeCab's grammar fields: part of speech and its first two sub-types (名詞 / 固有名詞 / 人名), the
/// conjugation type (五段・カ行イ音便) and the conjugated form (連用タ接続).
/// </summary>
internal readonly record struct DetailedToken(string Surface, string Lemma, string Reading, WordClass? Class,
    string Pos, string Pos1, string Pos2, string ConjugationType, string ConjugatedForm)
{
    public bool IsPunctuation => Pos == "記号";
    public bool IsFiller => Pos == "フィラー";
    public bool IsProperNoun => Pos == "名詞" && Pos1 == "固有名詞";
}

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

    public static List<JapaneseToken> Tokenize(string text) =>
        [.. TokenizeDetailed(text).Select(t => new JapaneseToken(t.Surface, t.Lemma, t.Reading, t.Class))];

    public static List<DetailedToken> TokenizeDetailed(string text)
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

        List<DetailedToken> tokens = new(nodes.Length);
        foreach (MeCabIpaDicNode node in nodes)
        {
            if (string.IsNullOrEmpty(node.Surface))
            {
                continue;
            }
            string reading = Field(node.Reading);
            if (Exclamations.Contains(node.Surface))
            {
                tokens.Add(new DetailedToken(node.Surface, node.Surface, reading, WordClass.Interjection, "感動詞", string.Empty, string.Empty, string.Empty, string.Empty));
                continue;
            }
            string lemma = Field(node.OriginalForm) is { Length: > 0 } original ? original : node.Surface;
            tokens.Add(new DetailedToken(node.Surface, lemma, reading, Classify(node.PartsOfSpeech, node.PartsOfSpeechSection1, lemma),
                Field(node.PartsOfSpeech), Field(node.PartsOfSpeechSection1), Field(node.PartsOfSpeechSection2),
                Field(node.ConjugatedForm), Field(node.Inflection)));
        }
        return tokens;
    }

    private static string Field(string? value) => string.IsNullOrEmpty(value) || value == "*" ? string.Empty : value;

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
