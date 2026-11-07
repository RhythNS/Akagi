using Akagi.Life.Japanese.Calls;
using System.Globalization;
using System.Text;

namespace Akagi.Life.Japanese.Mining;

/// <summary>
/// Which voices of a recording go into a clip or a subtitle file.
/// </summary>
internal enum MiningVoices
{
    /// <summary>Only the partner: their channel, and their lines in the subtitles.</summary>
    Partner,
    /// <summary>The whole conversation.</summary>
    Both,
    /// <summary>Only the learner's own channel, for clips of their own lines.</summary>
    Learner
}

/// <summary>
/// The text files of a mining export: an Anki import file for picked sentences and an SRT subtitle file for a call.
/// Kept free of audio work so they can be checked on their own.
/// </summary>
internal static class MiningExport
{
    /// <summary>Clips start this much before the line, so the first sound is not cut off.</summary>
    public const double LeadSeconds = 0.25;
    /// <summary>And end this much after it.</summary>
    public const double TailSeconds = 0.45;
    /// <summary>A transcript line can run long; a card's clip stays listenable.</summary>
    public const double MaxClipSeconds = 20;

    public const string CsvFileName = "akagi-mining.csv";
    public const string ReadmeFileName = "README.txt";

    public static (double Start, double End) ClipRange(MinedSentence sentence)
    {
        double start = Math.Max(0, sentence.Start - LeadSeconds);
        double end = Math.Max(sentence.End, sentence.Start) + TailSeconds;
        return (start, Math.Min(end, start + MaxClipSeconds));
    }

    /// <summary>
    /// "akagi_rii_20261103_0412_ab12cd.mp3": the partner, the day, the minute and second, and the sentence id, so names
    /// never clash in Anki's media folder.
    /// </summary>
    public static string ClipFileName(MinedSentence sentence)
    {
        string who = Slug(string.IsNullOrWhiteSpace(sentence.Speaker) ? "call" : sentence.Speaker);
        TimeSpan at = TimeSpan.FromSeconds(Math.Max(0, sentence.Start));
        string id = sentence.Id is { Length: >= 6 } full ? full[^6..] : Slug(sentence.Text).PadRight(6, 'x')[..6];
        return string.Create(CultureInfo.InvariantCulture,
            $"akagi_{who}_{sentence.Date:yyyyMMdd}_{(int)at.TotalMinutes:00}{at.Seconds:00}_{id}.mp3");
    }

    /// <summary>
    /// Anki's CSV import with header lines that name the separator and columns; Audio holds "[sound:file]" when a clip
    /// was cut.
    /// </summary>
    public static string AnkiCsv(IEnumerable<(MinedSentence Sentence, string? Clip)> rows)
    {
        StringBuilder csv = new();
        csv.AppendLine("#separator:Comma");
        csv.AppendLine("#html:false");
        csv.AppendLine("#columns:Sentence,Target,Audio,Source,Note");
        foreach ((MinedSentence sentence, string? clip) in rows)
        {
            string source = string.Create(CultureInfo.InvariantCulture,
                $"{sentence.CallName}, {sentence.Date:yyyy-MM-dd} {TranscriptChunker.Timestamp(sentence.Start)}");
            csv.AppendLine(string.Join(',',
                Field(sentence.Text),
                Field(sentence.Target),
                Field(clip == null ? string.Empty : $"[sound:{clip}]"),
                Field(source),
                Field(sentence.Note)));
        }
        return csv.ToString();
    }

    public static string Readme(int sentences, int clips, bool ffmpegMissing)
    {
        StringBuilder text = new();
        text.AppendLine($"{sentences} sentence(s), {clips} audio clip(s).");
        text.AppendLine();
        text.AppendLine("Importing into Anki:");
        text.AppendLine("1. Copy the .mp3 files into your Anki profile's collection.media folder");
        text.AppendLine("   (Anki: Tools > Check Media > View Files shows where it is).");
        text.AppendLine($"2. File > Import > {CsvFileName}. The columns are Sentence, Target, Audio, Source and Note;");
        text.AppendLine("   map them to the fields of your sentence card type.");
        if (ffmpegMissing)
        {
            text.AppendLine();
            text.AppendLine("No clips were cut because ffmpeg was not found on the server (CallAnalysis:FfmpegPath).");
        }
        else if (clips < sentences)
        {
            text.AppendLine();
            text.AppendLine("Some sentences have no clip: they come from a transcript without audio, or the recording is gone.");
        }
        return text.ToString();
    }

    /// <summary>
    /// The call's transcript as SRT. With <see cref="MiningVoices.Partner"/> only the partner's lines; with both voices
    /// each line is labeled with its speaker.
    /// </summary>
    public static string Srt(CallJob job, MiningVoices voices)
    {
        List<TranscriptSegment> segments = [.. job.Segments
            .Where(s => s.Text.Trim().Length > 0)
            .Where(s => voices == MiningVoices.Both || !job.HasLearnerSeparation || job.IsLearnerLine(s) == (voices == MiningVoices.Learner))
            .OrderBy(s => s.Start)];
        bool label = voices == MiningVoices.Both && job.HasLearnerSeparation;

        StringBuilder srt = new();
        for (int index = 0; index < segments.Count; index++)
        {
            TranscriptSegment segment = segments[index];
            double end = segment.End > segment.Start ? segment.End : Math.Min(segment.Start + 4, NextStart(segments, index) ?? segment.Start + 4);
            srt.AppendLine((index + 1).ToString(CultureInfo.InvariantCulture));
            srt.AppendLine($"{SrtTime(segment.Start)} --> {SrtTime(end)}");
            string speaker = job.IsLearnerLine(segment) ? CallJob.DefaultLearnerLabel : job.PartnerLabel;
            srt.AppendLine(label ? $"{speaker}: {segment.Text.Trim()}" : segment.Text.Trim());
            srt.AppendLine();
        }
        return srt.ToString();
    }

    public static string SrtTime(double seconds)
    {
        TimeSpan time = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000));
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
    }

    /// <summary>
    /// "Call with Rii 2026-11-03" as a file name.
    /// </summary>
    public static string CallFileName(CallJob job)
    {
        string who = string.IsNullOrWhiteSpace(job.Partner) ? Slug(job.Title) : Slug(job.Partner);
        return string.Create(CultureInfo.InvariantCulture, $"{(who.Length == 0 ? "call" : who)}_{job.RecordedAt ?? job.CreatedAt:yyyy-MM-dd}");
    }

    private static double? NextStart(List<TranscriptSegment> segments, int index) =>
        index + 1 < segments.Count ? segments[index + 1].Start : null;

    private static string Field(string value)
    {
        string single = value.ReplaceLineEndings(" ");
        return single.IndexOfAny([',', '"', '\n']) >= 0 || single.StartsWith('#') ? $"\"{single.Replace("\"", "\"\"")}\"" : single;
    }

    /// <summary>
    /// Letters and digits only, lower case; Japanese names stay as they are.
    /// </summary>
    internal static string Slug(string text)
    {
        StringBuilder slug = new();
        foreach (char c in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }
        string result = slug.ToString().Trim('-');
        return result.Length > 24 ? result[..24] : result;
    }
}
