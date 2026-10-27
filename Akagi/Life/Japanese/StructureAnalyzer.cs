namespace Akagi.Life.Japanese;

internal record RepeatedStructure(string Text, bool AtStart, int Count, int Sentences)
{
    public double Share => Sentences == 0 ? 0 : (double)Count / Sentences;

    public string Describe() => AtStart
        ? $"starts {Count} of {Sentences} sentences with '{Text}...'"
        : $"ends {Count} of {Sentences} sentences with '...{Text}'";
}

/// <summary>
/// Finds sentence openers and endings the user leans on, such as starting every sentence with でも or ending with と思います.
/// </summary>
internal static class StructureAnalyzer
{
    public const int MinSentences = 8;
    public const int MinCount = 3;
    public const double MinShare = 0.2;

    public static List<RepeatedStructure> Analyze(string text)
    {
        List<string> sentences = [.. JapaneseText.SplitSentences(text).Select(JapaneseText.TrimEnding).Where(s => s.Length >= 4)];
        if (sentences.Count < MinSentences)
        {
            return [];
        }

        List<RepeatedStructure> found = [];
        found.AddRange(Repeated(sentences, s => Openers(s), atStart: true));
        found.AddRange(Repeated(sentences, s => Endings(s), atStart: false));
        return [.. found.OrderByDescending(f => f.Count)];
    }

    private static IEnumerable<RepeatedStructure> Repeated(List<string> sentences, Func<string, IEnumerable<string>> pieces, bool atStart)
    {
        Dictionary<string, int> counts = [];
        foreach (string sentence in sentences)
        {
            foreach (string piece in pieces(sentence).Distinct())
            {
                counts[piece] = counts.GetValueOrDefault(piece) + 1;
            }
        }

        int threshold = Math.Max(MinCount, (int)Math.Ceiling(sentences.Count * MinShare));
        List<KeyValuePair<string, int>> frequent = [.. counts.Where(c => c.Value >= threshold)];

        // Keep the longest piece of each family: 'と思います' rather than also 'います' with the same count
        return frequent
            .Where(c => !frequent.Any(o => o.Key.Length > c.Key.Length && o.Value >= c.Value
                && (atStart ? o.Key.StartsWith(c.Key, StringComparison.Ordinal) : o.Key.EndsWith(c.Key, StringComparison.Ordinal))))
            .Select(c => new RepeatedStructure(c.Key, atStart, c.Value, sentences.Count));
    }

    /// <summary>
    /// The opening word: everything before the first comma when short, otherwise the first two to four characters.
    /// </summary>
    private static IEnumerable<string> Openers(string sentence)
    {
        int comma = sentence.IndexOfAny(['、', ',']);
        if (comma is > 0 and <= 6)
        {
            yield return sentence[..comma];
            yield break;
        }
        for (int length = 2; length <= Math.Min(4, sentence.Length - 1); length++)
        {
            yield return sentence[..length];
        }
    }

    /// <summary>
    /// Sentence frames learners lean on. Plain inflections such as ました are not habits, so only endings that contain
    /// one of these frames count.
    /// </summary>
    private static readonly string[] EndingFrames =
    [
        "と思", "かなと", "かなって", "んですけど", "んだけど", "けど", "みたいな", "感じ", "気がする", "気がします",
        "かもしれ", "っていう", "ということ", "んですよ", "んですね", "ですよね", "じゃないですか", "じゃん", "わけです",
        "ことです", "らしい", "ようです", "そうです"
    ];

    private static IEnumerable<string> Endings(string sentence)
    {
        for (int length = 2; length <= Math.Min(8, sentence.Length - 1); length++)
        {
            string ending = sentence[^length..];
            if (EndingFrames.Any(frame => ending.Contains(frame, StringComparison.Ordinal)))
            {
                yield return ending;
            }
        }
    }
}
