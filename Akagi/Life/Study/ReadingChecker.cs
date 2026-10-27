using System.Globalization;
using System.Text;

namespace Akagi.Life.Study;

/// <summary>
/// Compares the kana reading the user intended with what was heard (for example a voice note transcription)
/// mora by mora, and names the kind of slip: vowel length, small tsu, voicing marks, contracted sounds.
/// </summary>
internal static class ReadingChecker
{
    internal enum IssueKind
    {
        VowelLength,
        Geminate,
        Voicing,
        Contraction,
        Missing,
        Extra,
        Different
    }

    internal record Issue(IssueKind Kind, int Position, string Expected, string Heard)
    {
        public string Describe() => Kind switch
        {
            IssueKind.VowelLength => $"vowel length at kana {Position + 1}: expected '{Expected}', heard '{Heard}'",
            IssueKind.Geminate => $"small tsu (double consonant) at kana {Position + 1}: expected '{Expected}', heard '{Heard}'",
            IssueKind.Voicing => $"voicing (dakuten) at kana {Position + 1}: expected '{Expected}', heard '{Heard}'",
            IssueKind.Contraction => $"contracted sound at kana {Position + 1}: expected '{Expected}', heard '{Heard}'",
            IssueKind.Missing => $"missing '{Expected}' at kana {Position + 1}",
            IssueKind.Extra => $"extra '{Heard}' at kana {Position + 1}",
            _ => $"kana {Position + 1}: expected '{Expected}', heard '{Heard}'"
        };
    }

    internal record Result(double Score, IReadOnlyList<Issue> Issues, string Expected, string Heard)
    {
        public bool Perfect => Issues.Count == 0;

        public string Describe()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Expected: {Expected}");
            builder.AppendLine($"Heard:    {Heard}");
            builder.AppendLine($"Match: {Score:P0}");
            if (Perfect)
            {
                builder.AppendLine("No differences.");
            }
            foreach (Issue issue in Issues)
            {
                builder.AppendLine("- " + issue.Describe());
            }
            return builder.ToString().TrimEnd();
        }
    }

    private const string SmallKana = "ゃゅょぁぃぅぇぉゎ";
    private const string Vowels = "あいうえお";

    public static Result Check(string expected, string heard)
    {
        string normalizedExpected = Normalize(expected);
        string normalizedHeard = Normalize(heard);
        // Align kana by kana: then a missing small tsu, a long vowel or a large instead of small ya/yu/yo is one edit
        List<string> a = [.. normalizedExpected.Select(c => c.ToString())];
        List<string> b = [.. normalizedHeard.Select(c => c.ToString())];

        List<Issue> issues = [];
        foreach ((int position, string? x, string? y) in Align(a, b))
        {
            if (x == y)
            {
                continue;
            }
            issues.Add(new Issue(Classify(x, y, position, a), position, x ?? string.Empty, y ?? string.Empty));
        }

        double score = a.Count == 0 ? (b.Count == 0 ? 1 : 0) : Math.Max(0, 1.0 - (double)issues.Count / a.Count);
        return new Result(score, issues, normalizedExpected, normalizedHeard);
    }

    /// <summary>
    /// Katakana to hiragana, without whitespace and punctuation.
    /// </summary>
    public static string Normalize(string text)
    {
        StringBuilder builder = new();
        foreach (char c in text.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) && c != 'ー')
            {
                continue;
            }
            builder.Append(c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c);
        }
        return builder.ToString();
    }

    public static List<string> SplitMorae(string kana)
    {
        List<string> morae = [];
        foreach (char c in kana)
        {
            if (SmallKana.Contains(c) && morae.Count > 0)
            {
                morae[^1] += c;
            }
            else
            {
                morae.Add(c.ToString());
            }
        }
        return morae;
    }

    private static IssueKind Classify(string? expected, string? heard, int position, List<string> expectedMorae)
    {
        if (expected == null || heard == null)
        {
            string present = expected ?? heard!;
            if (present == "っ")
            {
                return IssueKind.Geminate;
            }
            if (present == "ー" || IsVowelExtension(present, position, expectedMorae))
            {
                return IssueKind.VowelLength;
            }
            return expected == null ? IssueKind.Extra : IssueKind.Missing;
        }

        if (expected == "っ" || heard == "っ")
        {
            return IssueKind.Geminate;
        }
        if (expected == "ー" || heard == "ー")
        {
            return IssueKind.VowelLength;
        }
        if (BaseOf(expected) == BaseOf(heard))
        {
            return IssueKind.Voicing;
        }
        if (Unsmall(expected) == Unsmall(heard))
        {
            return IssueKind.Contraction;
        }
        return IssueKind.Different;
    }

    /// <summary>
    /// A plain vowel right after a mora ending in the same (or a lengthening) vowel, as in おかあさん or こうこう.
    /// </summary>
    private static bool IsVowelExtension(string mora, int position, List<string> morae)
    {
        if (mora.Length != 1 || !Vowels.Contains(mora[0]))
        {
            return false;
        }
        int previous = Math.Min(position, morae.Count) - 1;
        if (previous < 0 || morae[previous].Length == 0)
        {
            return false;
        }

        char? previousVowel = VowelOf(morae[previous][^1]);
        return previousVowel == mora[0]
            || (previousVowel == 'お' && mora[0] == 'う')
            || (previousVowel == 'え' && mora[0] == 'い');
    }

    private static readonly string[] VowelRows =
    [
        "あかさたなはまやらわがざだばぱぁゃ",
        "いきしちにひみりぎじぢびぴぃ",
        "うくすつぬふむゆるぐずづぶぷぅゅ",
        "えけせてねへめれげぜでべぺぇ",
        "おこそとのほもよろをごぞどぼぽぉょ"
    ];

    private static char? VowelOf(char kana)
    {
        for (int i = 0; i < VowelRows.Length; i++)
        {
            if (VowelRows[i].Contains(kana))
            {
                return Vowels[i];
            }
        }
        return null;
    }

    private static string BaseOf(string mora)
    {
        StringBuilder builder = new();
        foreach (char c in mora.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static string Unsmall(string mora)
    {
        StringBuilder builder = new();
        foreach (char c in mora)
        {
            int index = SmallKana.IndexOf(c);
            builder.Append(index >= 0 ? "やゆよあいうえおわ"[index] : c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// Levenshtein alignment. Yields (expected position, expected mora or null, heard mora or null).
    /// </summary>
    private static IEnumerable<(int Position, string? Expected, string? Heard)> Align(List<string> a, List<string> b)
    {
        int[,] cost = new int[a.Count + 1, b.Count + 1];
        for (int i = 0; i <= a.Count; i++)
        {
            cost[i, 0] = i;
        }
        for (int j = 0; j <= b.Count; j++)
        {
            cost[0, j] = j;
        }
        for (int i = 1; i <= a.Count; i++)
        {
            for (int j = 1; j <= b.Count; j++)
            {
                int substitution = cost[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                cost[i, j] = Math.Min(substitution, Math.Min(cost[i - 1, j] + 1, cost[i, j - 1] + 1));
            }
        }

        List<(int, string?, string?)> steps = [];
        int x = a.Count;
        int y = b.Count;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && cost[x, y] == cost[x - 1, y - 1] + (a[x - 1] == b[y - 1] ? 0 : 1))
            {
                steps.Add((x - 1, a[x - 1], b[y - 1]));
                x--;
                y--;
            }
            else if (x > 0 && cost[x, y] == cost[x - 1, y] + 1)
            {
                steps.Add((x - 1, a[x - 1], null));
                x--;
            }
            else
            {
                steps.Add((x, null, b[y - 1]));
                y--;
            }
        }
        steps.Reverse();
        return steps;
    }
}
