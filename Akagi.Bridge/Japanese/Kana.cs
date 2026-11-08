using System.Text;
using System.Text.RegularExpressions;

namespace Akagi.Bridge.Japanese;

/// <summary>
/// A piece of written Japanese and, for kanji, its reading.
/// </summary>
public readonly record struct FuriganaPart(string Text, string? Ruby);

/// <summary>
/// Kana conversions for typed answers: romaji and katakana become hiragana, so an answer can be typed with or without
/// a Japanese keyboard.
/// </summary>
public static class Kana
{
    private static readonly Dictionary<string, string> Romaji = BuildRomaji();
    private static readonly int LongestRomaji = Romaji.Keys.Max(k => k.Length);

    /// <summary>
    /// The answer as hiragana: trimmed, without spaces, romaji and katakana converted, full-width letters made plain.
    /// </summary>
    public static string Normalize(string text)
    {
        string compact = new([.. text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c))]);
        return ToHiragana(FromRomaji(compact));
    }

    public static string ToHiragana(string text)
    {
        StringBuilder builder = new(text.Length);
        foreach (char c in text)
        {
            builder.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c);
        }
        return builder.ToString();
    }

    public static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヺ' or 'ー' or 'ゝ' or 'ゞ' or 'ヽ' or 'ヾ';

    /// <summary>
    /// Converts the romaji in a text to hiragana and leaves everything else as it is. Takes Hepburn and Kunrei
    /// spellings, nn or n' for ん, and a doubled consonant for っ.
    /// </summary>
    public static string FromRomaji(string text)
    {
        string lower = text.ToLowerInvariant();
        StringBuilder builder = new(lower.Length);
        int i = 0;
        while (i < lower.Length)
        {
            char c = lower[i];
            if (c is < 'a' or > 'z' && c != '-' && c != '\'')
            {
                builder.Append(text[i]);
                i++;
                continue;
            }
            // っ: a doubled consonant (kk, tt, tch)
            if (i + 1 < lower.Length && IsConsonant(c) && c != 'n' && (lower[i + 1] == c || (c == 't' && lower[i + 1] == 'c')))
            {
                builder.Append('っ');
                i++;
                continue;
            }
            if (c == 'n')
            {
                char next = i + 1 < lower.Length ? lower[i + 1] : '\0';
                if (next == '\'')
                {
                    builder.Append('ん');
                    i += 2;
                    continue;
                }
                if (next == 'n')
                {
                    // konnichiha: the second n starts に; nn at the end or before a consonant is one ん
                    char after = i + 2 < lower.Length ? lower[i + 2] : '\0';
                    builder.Append('ん');
                    i += after is 'a' or 'i' or 'u' or 'e' or 'o' or 'y' ? 1 : 2;
                    continue;
                }
                if (next is not ('a' or 'i' or 'u' or 'e' or 'o' or 'y'))
                {
                    builder.Append('ん');
                    i++;
                    continue;
                }
            }
            bool matched = false;
            for (int length = Math.Min(LongestRomaji, lower.Length - i); length > 0; length--)
            {
                if (Romaji.TryGetValue(lower.Substring(i, length), out string? kana))
                {
                    builder.Append(kana);
                    i += length;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                builder.Append(text[i]);
                i++;
            }
        }
        return builder.ToString();
    }

    private static bool IsConsonant(char c) => c is >= 'a' and <= 'z' and not ('a' or 'i' or 'u' or 'e' or 'o');

    /// <summary>
    /// Splits a written word into kana and kanji parts with the reading of each kanji part, by matching the kana
    /// around the kanji against the reading (取り消す + とりけす: 取/と, り, 消/け, す). When they cannot be
    /// matched the whole word gets the reading.
    /// </summary>
    public static IReadOnlyList<FuriganaPart> Furigana(string written, string reading)
    {
        if (written.Length == 0 || written.All(IsKana))
        {
            return [new FuriganaPart(written, null)];
        }
        List<(string Text, bool Kana)> runs = [];
        foreach (char c in written)
        {
            bool kana = IsKana(c);
            if (runs.Count > 0 && runs[^1].Kana == kana)
            {
                runs[^1] = (runs[^1].Text + c, kana);
            }
            else
            {
                runs.Add((c.ToString(), kana));
            }
        }
        string pattern = "^" + string.Concat(runs.Select(r => r.Kana ? Regex.Escape(ToHiragana(r.Text)) : "(.+?)")) + "$";
        Match match = Regex.Match(ToHiragana(reading), pattern);
        if (!match.Success)
        {
            return [new FuriganaPart(written, reading)];
        }
        List<FuriganaPart> parts = [];
        int group = 1;
        foreach ((string text, bool kana) in runs)
        {
            parts.Add(kana ? new FuriganaPart(text, null) : new FuriganaPart(text, match.Groups[group++].Value));
        }
        return parts;
    }

    private static Dictionary<string, string> BuildRomaji()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal)
        {
            ["a"] = "あ", ["i"] = "い", ["u"] = "う", ["e"] = "え", ["o"] = "お",
            ["ka"] = "か", ["ki"] = "き", ["ku"] = "く", ["ke"] = "け", ["ko"] = "こ",
            ["ga"] = "が", ["gi"] = "ぎ", ["gu"] = "ぐ", ["ge"] = "げ", ["go"] = "ご",
            ["sa"] = "さ", ["shi"] = "し", ["si"] = "し", ["su"] = "す", ["se"] = "せ", ["so"] = "そ",
            ["za"] = "ざ", ["ji"] = "じ", ["zi"] = "じ", ["zu"] = "ず", ["ze"] = "ぜ", ["zo"] = "ぞ",
            ["ta"] = "た", ["chi"] = "ち", ["ti"] = "ち", ["tsu"] = "つ", ["tu"] = "つ", ["te"] = "て", ["to"] = "と",
            ["da"] = "だ", ["di"] = "ぢ", ["du"] = "づ", ["de"] = "で", ["do"] = "ど",
            ["na"] = "な", ["ni"] = "に", ["nu"] = "ぬ", ["ne"] = "ね", ["no"] = "の",
            ["ha"] = "は", ["hi"] = "ひ", ["fu"] = "ふ", ["hu"] = "ふ", ["he"] = "へ", ["ho"] = "ほ",
            ["ba"] = "ば", ["bi"] = "び", ["bu"] = "ぶ", ["be"] = "べ", ["bo"] = "ぼ",
            ["pa"] = "ぱ", ["pi"] = "ぴ", ["pu"] = "ぷ", ["pe"] = "ぺ", ["po"] = "ぽ",
            ["ma"] = "ま", ["mi"] = "み", ["mu"] = "む", ["me"] = "め", ["mo"] = "も",
            ["ya"] = "や", ["yu"] = "ゆ", ["yo"] = "よ",
            ["ra"] = "ら", ["ri"] = "り", ["ru"] = "る", ["re"] = "れ", ["ro"] = "ろ",
            ["wa"] = "わ", ["wi"] = "ゐ", ["we"] = "ゑ", ["wo"] = "を",
            ["n"] = "ん",
            ["sha"] = "しゃ", ["shu"] = "しゅ", ["sho"] = "しょ", ["she"] = "しぇ",
            ["cha"] = "ちゃ", ["chu"] = "ちゅ", ["cho"] = "ちょ", ["che"] = "ちぇ",
            ["ja"] = "じゃ", ["ju"] = "じゅ", ["jo"] = "じょ", ["je"] = "じぇ",
            ["fa"] = "ふぁ", ["fi"] = "ふぃ", ["fe"] = "ふぇ", ["fo"] = "ふぉ",
            ["va"] = "ゔぁ", ["vi"] = "ゔぃ", ["vu"] = "ゔ", ["ve"] = "ゔぇ", ["vo"] = "ゔぉ",
            ["xa"] = "ぁ", ["xi"] = "ぃ", ["xu"] = "ぅ", ["xe"] = "ぇ", ["xo"] = "ぉ",
            ["la"] = "ぁ", ["li"] = "ぃ", ["lu"] = "ぅ", ["le"] = "ぇ", ["lo"] = "ぉ",
            ["xya"] = "ゃ", ["xyu"] = "ゅ", ["xyo"] = "ょ", ["lya"] = "ゃ", ["lyu"] = "ゅ", ["lyo"] = "ょ",
            ["xtsu"] = "っ", ["xtu"] = "っ", ["ltsu"] = "っ", ["ltu"] = "っ",
            ["-"] = "ー"
        };
        // ky, gy, sy, zy, ty, dy, ny, hy, by, py, my, ry + a/u/o, from the i-column kana
        foreach ((string start, string kana) in new[]
        {
            ("k", "き"), ("g", "ぎ"), ("s", "し"), ("z", "じ"), ("j", "じ"), ("t", "ち"), ("c", "ち"), ("d", "ぢ"), ("n", "に"),
            ("h", "ひ"), ("b", "び"), ("p", "ぴ"), ("m", "み"), ("r", "り")
        })
        {
            map.TryAdd(start + "ya", kana + "ゃ");
            map.TryAdd(start + "yu", kana + "ゅ");
            map.TryAdd(start + "yo", kana + "ょ");
        }
        return map;
    }
}
