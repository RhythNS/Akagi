using System.Text;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// One voice of a call: its words, and its loudness for telling real speech from a trace of the other voice.
/// </summary>
internal record ChannelTranscript(IReadOnlyList<TranscriptWord> Words, double[]? Loudness);

/// <summary>
/// Turns separately transcribed channels into one conversation. The channel says who spoke, which is more reliable than
/// speaker detection, and a transcript of both voices mixed serves as a second hearing: alone on a channel the model has
/// no context, so it can miss what the other voice makes clear, while mixed it tends to smooth over real slips. Neither is
/// always right, so differences are noted on the line instead of being resolved here.
/// </summary>
internal static class ConversationMerger
{
    /// <summary>
    /// A pause this long ends a phrase.
    /// </summary>
    public const double PhraseGapSeconds = 1.0;
    public const double LoudnessWindowSeconds = 0.05;
    /// <summary>
    /// The other voice leaks faintly into a channel, and the transcriber picks it up during silence. Such phrases sit at
    /// the noise floor, around 1% of real speech, while the quietest real speech measured was over 10%.
    /// </summary>
    public const double LeakRatio = 0.05;
    private const double SpeechLevelPercentile = 0.95;
    /// <summary>
    /// Backchannels like うん are not worth a second opinion.
    /// </summary>
    private const int MinComparedCharacters = 4;
    private const int MaxNotes = 5;
    private const int MaxNoteCharacters = 14;
    private const double HearingPaddingSeconds = 0.3;
    private const string Punctuation = "、。，．,.？！?!…‥・~〜-ー ";

    private sealed class Phrase
    {
        public required double Start { get; init; }
        public double End { get; set; }
        public StringBuilder Text { get; } = new();
    }

    public static List<TranscriptSegment> Merge(ChannelTranscript learner, ChannelTranscript partner, IReadOnlyList<TranscriptWord> mix,
        string learnerLabel, string partnerLabel)
    {
        List<TranscriptSegment> segments = [.. Voice(learner, partner, mix, learnerLabel), .. Voice(partner, learner, mix, partnerLabel)];
        return [.. segments.OrderBy(s => s.Start)];
    }

    private static IEnumerable<TranscriptSegment> Voice(ChannelTranscript own, ChannelTranscript other, IReadOnlyList<TranscriptWord> mix, string label)
    {
        double level = SpeechLevel(own.Loudness);
        foreach (Phrase phrase in Phrases(own.Words))
        {
            string text = phrase.Text.ToString().Trim();
            if (Clean(text).Length == 0 || IsLeak(phrase, own.Loudness, other.Loudness, level))
            {
                continue;
            }
            yield return new TranscriptSegment
            {
                Start = phrase.Start,
                End = phrase.End,
                Speaker = label,
                Text = text,
                Disputed = SecondHearing(text, phrase, mix)
            };
        }
    }

    private static List<Phrase> Phrases(IReadOnlyList<TranscriptWord> words)
    {
        List<Phrase> phrases = [];
        bool closed = true;
        foreach (TranscriptWord word in words)
        {
            if (closed || word.Start - phrases[^1].End >= PhraseGapSeconds)
            {
                phrases.Add(new Phrase { Start = word.Start });
            }
            phrases[^1].End = word.End;
            phrases[^1].Text.Append(word.Text);
            closed = word.Text.TrimEnd().EndsWith('。') || word.Text.TrimEnd().EndsWith('？') || word.Text.TrimEnd().EndsWith('?');
        }
        return phrases;
    }

    private static bool IsLeak(Phrase phrase, double[]? own, double[]? other, double level)
    {
        if (own == null || other == null)
        {
            return false;
        }
        return Loudness(own, phrase) < LeakRatio * Math.Max(level, Loudness(other, phrase));
    }

    private static double SpeechLevel(double[]? loudness)
    {
        if (loudness == null || loudness.Length == 0)
        {
            return 0;
        }
        double[] sorted = [.. loudness.Order()];
        return sorted[(int)((sorted.Length - 1) * SpeechLevelPercentile)];
    }

    private static double Loudness(double[] loudness, Phrase phrase)
    {
        int first = Math.Clamp((int)(phrase.Start / LoudnessWindowSeconds), 0, loudness.Length);
        int last = Math.Clamp((int)Math.Ceiling(phrase.End / LoudnessWindowSeconds), first, loudness.Length);
        if (last == first)
        {
            return 0;
        }
        double sum = 0;
        for (int i = first; i < last; i++)
        {
            sum += loudness[i] * loudness[i];
        }
        return Math.Sqrt(sum / (last - first));
    }

    private static string SecondHearing(string text, Phrase phrase, IReadOnlyList<TranscriptWord> mix)
    {
        string spoken = Clean(text);
        if (spoken.Length < MinComparedCharacters || mix.Count == 0)
        {
            return string.Empty;
        }
        string heard = Clean(string.Concat(mix
            .Where(w => w.Start >= phrase.Start - HearingPaddingSeconds && w.End <= phrase.End + HearingPaddingSeconds)
            .Select(w => w.Text)));
        return string.Join("; ", Differences(spoken, heard).Take(MaxNotes).Select(d => $"{d.Text} -> {(d.Heard.Length == 0 ? "(nothing)" : Shorten(d.Heard))}"));
    }

    private static string Shorten(string text) => text.Length > MaxNoteCharacters ? text[..MaxNoteCharacters] + "..." : text;

    /// <summary>
    /// The parts of <paramref name="text"/> that <paramref name="heard"/> has differently, each with the character before
    /// it as an anchor. Words only in <paramref name="heard"/> are left out: the second hearing also holds the other voice.
    /// </summary>
    internal static List<(string Text, string Heard)> Differences(string text, string heard)
    {
        int[,] common = new int[text.Length + 1, heard.Length + 1];
        for (int i = text.Length - 1; i >= 0; i--)
        {
            for (int j = heard.Length - 1; j >= 0; j--)
            {
                common[i, j] = text[i] == heard[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        List<(string, string)> differences = [];
        StringBuilder missing = new();
        StringBuilder instead = new();
        string anchor = string.Empty;
        int a = 0;
        int b = 0;

        void Flush()
        {
            if (missing.Length >= 2 || (missing.Length == 1 && instead.Length > 0))
            {
                differences.Add((anchor + missing, anchor + instead));
            }
            missing.Clear();
            instead.Clear();
        }

        while (a < text.Length || b < heard.Length)
        {
            if (a < text.Length && b < heard.Length && text[a] == heard[b])
            {
                Flush();
                anchor = text[a].ToString();
                a++;
                b++;
            }
            else if (b < heard.Length && (a == text.Length || common[a, b + 1] >= common[a + 1, b]))
            {
                instead.Append(heard[b++]);
            }
            else
            {
                missing.Append(text[a++]);
            }
        }
        Flush();
        return differences;
    }

    private static string Clean(string text) => new([.. text.Where(c => !Punctuation.Contains(c))]);
}
