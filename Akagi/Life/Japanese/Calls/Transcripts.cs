using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// Reads transcripts the user brings from elsewhere (phone recorder apps, Whisper desktop, subtitle exports):
/// SRT, WebVTT, or plain text with optional "[hh:mm:ss]" timestamps and "Speaker:" labels.
/// </summary>
internal static partial class TranscriptParser
{
    public static readonly string[] Extensions = [".srt", ".vtt", ".txt", ".md"];

    public static bool IsTranscriptFile(string fileName) =>
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public static List<TranscriptSegment> Parse(string content, string fileName)
    {
        string normalized = content.Replace("\r\n", "\n").TrimStart('﻿');
        bool cues = CueTiming().IsMatch(normalized);
        List<TranscriptSegment> segments = cues ? ParseCues(normalized) : ParsePlain(normalized);
        return [.. segments.Where(s => s.Text.Length > 0)];
    }

    /// <summary>
    /// SRT and WebVTT: blocks with a "start --> end" timing line followed by text lines.
    /// </summary>
    private static List<TranscriptSegment> ParseCues(string content)
    {
        List<TranscriptSegment> segments = [];
        foreach (string block in content.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string[] lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            int timing = Array.FindIndex(lines, l => CueTiming().IsMatch(l));
            if (timing < 0)
            {
                continue;
            }
            Match match = CueTiming().Match(lines[timing]);
            string text = string.Join(" ", lines.Skip(timing + 1).Select(StripTags));
            (string speaker, string spoken) = SplitSpeaker(text);
            segments.Add(new TranscriptSegment
            {
                Start = ParseTimestamp(match.Groups["start"].Value),
                End = ParseTimestamp(match.Groups["end"].Value),
                Speaker = speaker,
                Text = spoken
            });
        }
        return segments;
    }

    private static List<TranscriptSegment> ParsePlain(string content)
    {
        List<TranscriptSegment> segments = [];
        double lastTime = 0;
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            double start = lastTime;
            Match stamp = LineTimestamp().Match(line);
            if (stamp.Success)
            {
                start = ParseTimestamp(stamp.Groups["time"].Value);
                line = line[stamp.Length..].Trim();
            }
            (string speaker, string spoken) = SplitSpeaker(line);
            if (segments.Count > 0 && segments[^1].End < start)
            {
                segments[^1].End = start;
            }
            segments.Add(new TranscriptSegment { Start = start, End = start, Speaker = speaker, Text = spoken });
            lastTime = start;
        }
        return segments;
    }

    /// <summary>
    /// "Yuki: そうなんだ" becomes speaker "Yuki". Only short labels count, so a sentence with a colon stays intact.
    /// </summary>
    internal static (string Speaker, string Text) SplitSpeaker(string line)
    {
        Match match = SpeakerLabel().Match(line);
        return match.Success ? (match.Groups["speaker"].Value.Trim(), line[match.Length..].Trim()) : (string.Empty, line.Trim());
    }

    internal static double ParseTimestamp(string text)
    {
        string[] parts = text.Replace(',', '.').Split(':');
        double seconds = 0;
        foreach (string part in parts)
        {
            seconds = seconds * 60 + double.Parse(part, CultureInfo.InvariantCulture);
        }
        return seconds;
    }

    private static string StripTags(string line) => Tag().Replace(line, string.Empty).Trim();

    [GeneratedRegex(@"(?<start>(\d{1,2}:)?\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*(?<end>(\d{1,2}:)?\d{1,2}:\d{2}[.,]\d{1,3})")]
    private static partial Regex CueTiming();

    [GeneratedRegex(@"^[\[(]?(?<time>(\d{1,2}:)?\d{1,2}:\d{2}([.,]\d{1,3})?)[\])]?\s*")]
    private static partial Regex LineTimestamp();

    [GeneratedRegex(@"^(?![\d\s]+[:：])(?<speaker>[^:：\s][^:：]{0,19})[:：]\s*(?=\S)")]
    private static partial Regex SpeakerLabel();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}

internal static class TranscriptChunker
{
    /// <summary>
    /// Groups consecutive segments into chunks of about <paramref name="maxCharacters"/>, never splitting a segment,
    /// so each analysis call sees a coherent stretch of the conversation with speaker labels and timestamps.
    /// </summary>
    public static List<CallChunk> Chunk(IReadOnlyList<TranscriptSegment> segments, int maxCharacters)
    {
        List<CallChunk> chunks = [];
        StringBuilder text = new();
        double start = 0;
        double end = 0;

        foreach (TranscriptSegment segment in segments)
        {
            string line = Format(segment);
            if (text.Length > 0 && text.Length + line.Length > maxCharacters)
            {
                chunks.Add(new CallChunk { Index = chunks.Count, Start = start, End = end, Text = text.ToString().TrimEnd() });
                text.Clear();
            }
            if (text.Length == 0)
            {
                start = segment.Start;
            }
            text.AppendLine(line);
            end = Math.Max(end, segment.End);
        }

        if (text.Length > 0)
        {
            chunks.Add(new CallChunk { Index = chunks.Count, Start = start, End = end, Text = text.ToString().TrimEnd() });
        }
        return chunks;
    }

    public static string Format(TranscriptSegment segment)
    {
        string time = TimeSpan.FromSeconds(segment.Start).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(segment.Speaker) ? $"[{time}] {segment.Text}" : $"[{time}] {segment.Speaker}: {segment.Text}";
    }
}
