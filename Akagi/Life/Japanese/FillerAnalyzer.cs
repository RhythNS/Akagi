using System.Text.RegularExpressions;

namespace Akagi.Life.Japanese;

internal enum FillerKind
{
    /// <summary>A filled pause while searching for words: えーと, あのー, うーん.</summary>
    Hesitation,
    /// <summary>A hedge or discourse word used out of habit: なんか, やっぱり, 一応.</summary>
    Crutch
}

internal record FillerCount(string Form, FillerKind Kind, int Count, string Example);

internal class FillerReport
{
    public required int JapaneseCharacters { get; init; }
    public required IReadOnlyList<FillerCount> Counts { get; init; }

    public int Hesitations => Counts.Where(c => c.Kind == FillerKind.Hesitation).Sum(c => c.Count);
    public int Crutches => Counts.Where(c => c.Kind == FillerKind.Crutch).Sum(c => c.Count);

    /// <summary>
    /// Fillers per 100 Japanese characters; around 1-2 is natural conversation, above 4 starts to sound halting.
    /// </summary>
    public double Per100 => JapaneseCharacters == 0 ? 0 : (Hesitations + Crutches) * 100.0 / JapaneseCharacters;

    /// <summary>
    /// Words used so often that they turn into a habit: at least three times and more than once per 250 characters.
    /// </summary>
    public IEnumerable<FillerCount> Habits => Counts.Where(c => c.Count >= 3 && c.Count * 250.0 / Math.Max(1, JapaneseCharacters) > 1);
}

/// <summary>
/// Counts filled pauses and crutch words in Japanese speech. Some forms (あの, その, ちょっと) are also normal words,
/// so they only count in their filler shapes: lengthened (あのー) or followed by a pause mark (あの、).
/// </summary>
internal static class FillerAnalyzer
{
    private static readonly (string Canonical, FillerKind Kind, string Pattern)[] Forms =
    [
        ("えーと", FillerKind.Hesitation, "えーっと|えっと|えーと|ええと"),
        ("えー", FillerKind.Hesitation, "えー+(?![と])"),
        ("あー", FillerKind.Hesitation, "(?<![ゃゅょ])あー+(?=[、。…\\s,]|$)"),
        ("うーん", FillerKind.Hesitation, "うーん|ううん(?=[、…])|んー+"),
        ("あの", FillerKind.Hesitation, "あのー+|あの(?=[、…,\\s])"),
        ("その", FillerKind.Hesitation, "そのー+|その(?=[、…,])"),
        ("まあ", FillerKind.Crutch, "まあ|まぁ"),
        ("なんか", FillerKind.Crutch, "なんか"),
        ("っていうか", FillerKind.Crutch, "っていうか|ていうか|てか(?=[、\\s])"),
        ("なんていうか", FillerKind.Crutch, "なんていうか|何ていうか|なんというか"),
        ("やっぱり", FillerKind.Crutch, "やっぱり|やっぱ|やはり"),
        ("一応", FillerKind.Crutch, "一応|いちおう"),
        ("とりあえず", FillerKind.Crutch, "とりあえず"),
        ("普通に", FillerKind.Crutch, "普通に|ふつうに"),
        ("みたいな", FillerKind.Crutch, "みたいな(?=[、。…\\s]|$)"),
        ("ちょっと", FillerKind.Crutch, "ちょっと"),
        ("本当に", FillerKind.Crutch, "本当に|ほんとに"),
        ("確かに", FillerKind.Crutch, "確かに|たしかに"),
        ("なるほど", FillerKind.Crutch, "なるほど")
    ];

    private static readonly Regex Matcher = new(
        string.Join("|", Forms.Select((f, i) => $"(?<f{i}>{f.Pattern})")),
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static FillerReport Analyze(string text)
    {
        Dictionary<int, (int Count, string Example)> counts = [];
        foreach (Match match in Matcher.Matches(text))
        {
            for (int i = 0; i < Forms.Length; i++)
            {
                if (match.Groups[$"f{i}"].Success)
                {
                    counts.TryGetValue(i, out (int Count, string Example) current);
                    counts[i] = (current.Count + 1, current.Count == 0 ? JapaneseText.Snippet(text, match.Index, match.Length) : current.Example);
                    break;
                }
            }
        }

        return new FillerReport
        {
            JapaneseCharacters = JapaneseText.CountJapanese(text),
            Counts = [.. counts
                .Select(c => new FillerCount(Forms[c.Key].Canonical, Forms[c.Key].Kind, c.Value.Count, c.Value.Example))
                .OrderByDescending(c => c.Count)]
        };
    }
}
