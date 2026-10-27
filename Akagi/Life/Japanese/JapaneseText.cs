using System.Text.RegularExpressions;

namespace Akagi.Life.Japanese;

internal static partial class JapaneseText
{
    /// <summary>
    /// Splits speech or chat text into sentences at Japanese and Western sentence punctuation and line breaks.
    /// </summary>
    public static List<string> SplitSentences(string text)
    {
        List<string> sentences = [];
        foreach (string part in SentenceEnd().Split(text))
        {
            string sentence = part.Trim().Trim('「', '」', '"', '\'', '(', ')', '（', '）').Trim();
            if (CountJapanese(sentence) > 0)
            {
                sentences.Add(sentence);
            }
        }
        return sentences;
    }

    public static bool IsJapanese(char c) =>
        c is >= '぀' and <= 'ヿ'   // hiragana, katakana
        or >= '一' and <= '鿿'    // common kanji
        or >= '㐀' and <= '䶿'    // rare kanji
        or '々' or 'ー';

    public static int CountJapanese(string text) => text.Count(IsJapanese);

    /// <summary>
    /// Removes trailing punctuation, ellipses and laughter marks so sentence endings can be compared.
    /// </summary>
    public static string TrimEnding(string sentence) => TrailingNoise().Replace(sentence.Trim(), string.Empty);

    /// <summary>
    /// A short stretch of text around a match, used as evidence in the diagnosis.
    /// </summary>
    public static string Snippet(string text, int index, int length, int context = 12)
    {
        int start = Math.Max(0, index - context);
        int end = Math.Min(text.Length, index + length + context);
        return text[start..end].Replace('\n', ' ').Trim();
    }

    [GeneratedRegex(@"(?<=[。！？!?])|\r?\n")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"[。、！？!?…‥・.,\s〜～ーw笑]+$")]
    private static partial Regex TrailingNoise();
}
