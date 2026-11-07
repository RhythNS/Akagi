using Akagi.Life.Japanese.Calls;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;

namespace Akagi.Life.Japanese.Mining;

/// <summary>
/// A file made for the user to download.
/// </summary>
internal record MiningFile(string FileName, byte[] Data);

/// <summary>
/// Keeps the user's mined sentences and builds the downloads: picked sentences as audio clips with an Anki import
/// file, and a whole call as SRT subtitles with its audio for subtitle based mining tools.
/// </summary>
internal class MiningService
{
    public const int MaxSentences = 2000;

    private readonly ICallJobDatabase _jobs;
    private readonly IMinedSentenceDatabase _mined;
    private readonly IAudioClipper _clipper;
    private readonly ILogger _logger;

    public MiningService(ICallJobDatabase jobs, IMinedSentenceDatabase mined, IAudioClipper clipper, ILogger logger)
    {
        _jobs = jobs;
        _mined = mined;
        _clipper = clipper;
        _logger = logger;
    }

    public async Task<List<MinedSentence>> ListAsync(string userId) =>
        [.. (await _mined.GetForUser(userId)).OrderByDescending(m => m.AddedAt)];

    /// <summary>
    /// Mines the line of a call that starts at <paramref name="time"/>. <paramref name="text"/> may be the whole line or one
    /// sentence of it; the clip covers the line. Mining the same thing twice returns the first one.
    /// </summary>
    public async Task<(MinedSentence? Sentence, string? Error)> AddAsync(string userId, string callId, double time, string text, string target, string note)
    {
        CallJob? job = (await _jobs.GetForUser(userId)).FirstOrDefault(j => j.Id == callId);
        if (job == null)
        {
            return (null, "That call does not exist.");
        }
        TranscriptSegment? segment = FindSegment(job, time, text);
        if (segment == null)
        {
            return (null, "That line is not in the call's transcript.");
        }

        string sentence = string.IsNullOrWhiteSpace(text) ? segment.Text.Trim() : text.Trim();
        List<MinedSentence> existing = await _mined.GetForUser(userId);
        if (existing.FirstOrDefault(m => m.CallId == callId && Math.Abs(m.Start - segment.Start) < 0.01 && m.Text == sentence) is MinedSentence known)
        {
            return (known, null);
        }
        if (existing.Count >= MaxSentences)
        {
            return (null, $"Your mining list is full ({MaxSentences}). Remove some first.");
        }

        MinedSentence mined = new()
        {
            UserId = userId,
            CallId = callId,
            CallName = CallName(job),
            Date = job.RecordedAt ?? job.CreatedAt,
            Start = segment.Start,
            End = EndOf(job, segment),
            Speaker = job.HasLearnerSeparation && job.IsLearnerLine(segment) ? CallJob.DefaultLearnerLabel : job.PartnerLabel,
            IsLearner = job.HasLearnerSeparation && job.IsLearnerLine(segment),
            Text = sentence,
            Target = target.Trim(),
            Note = note.Trim(),
            AddedAt = DateTime.UtcNow
        };
        await _mined.SaveDocumentAsync(mined);
        return (mined, null);
    }

    public async Task<bool> RemoveAsync(string userId, string id)
    {
        MinedSentence? sentence = await _mined.GetDocumentByIdAsync(id);
        if (sentence == null || sentence.UserId != userId)
        {
            return false;
        }
        await _mined.DeleteDocumentByIdAsync(id);
        return true;
    }

    public async Task<bool> SetNoteAsync(string userId, string id, string note)
    {
        MinedSentence? sentence = await _mined.GetDocumentByIdAsync(id);
        if (sentence == null || sentence.UserId != userId)
        {
            return false;
        }
        sentence.Note = note.Trim();
        await _mined.SaveDocumentAsync(sentence);
        return true;
    }

    /// <summary>
    /// A zip with one clip per sentence, cut from the speaker's own channel, and the Anki import file. All sentences
    /// when <paramref name="ids"/> is empty.
    /// </summary>
    public async Task<(MiningFile? File, string? Error)> ExportClipsAsync(string userId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        List<MinedSentence> sentences = [.. (await _mined.GetForUser(userId))
            .Where(m => ids.Count == 0 || ids.Contains(m.Id!))
            .OrderBy(m => m.Date).ThenBy(m => m.Start)];
        if (sentences.Count == 0)
        {
            return (null, "There is nothing to export yet. Mine some sentences first.");
        }

        Dictionary<string, CallJob> jobs = (await _jobs.GetForUser(userId)).Where(j => j.Id != null).ToDictionary(j => j.Id!);
        bool ffmpeg = await _clipper.IsAvailableAsync(cancellationToken);
        string work = Path.Combine(Path.GetTempPath(), "akagi-mining-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            List<(MinedSentence Sentence, string? Clip)> rows = [];
            foreach (MinedSentence sentence in sentences)
            {
                string? clip = null;
                if (ffmpeg && jobs.TryGetValue(sentence.CallId, out CallJob? job) && HasAudio(job))
                {
                    string name = MiningExport.ClipFileName(sentence);
                    (double start, double end) = MiningExport.ClipRange(sentence);
                    // A sentence comes from one voice, so its clip is cut from that voice's channel
                    MiningVoices voices = sentence.IsLearner ? MiningVoices.Learner : MiningVoices.Partner;
                    try
                    {
                        await _clipper.ClipAsync(job.SourcePath, start, end, job.LearnerChannel, voices, Path.Combine(work, name), cancellationToken);
                        clip = name;
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "Could not cut the clip of mined sentence {SentenceId}", sentence.Id);
                    }
                }
                rows.Add((sentence, clip));
            }

            using MemoryStream zip = new();
            using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddText(archive, MiningExport.CsvFileName, "﻿" + MiningExport.AnkiCsv(rows));
                AddText(archive, MiningExport.ReadmeFileName, MiningExport.Readme(rows.Count, rows.Count(r => r.Clip != null), !ffmpeg));
                foreach (string clip in rows.Select(r => r.Clip).OfType<string>())
                {
                    archive.CreateEntryFromFile(Path.Combine(work, clip), clip, CompressionLevel.NoCompression);
                }
            }
            return (new MiningFile($"akagi-mining-{DateTime.Now:yyyy-MM-dd}.zip", zip.ToArray()), null);
        }
        finally
        {
            TryDelete(work);
        }
    }

    /// <summary>
    /// A zip with the call's subtitles and audio: the partner's channel and lines, or the whole conversation.
    /// </summary>
    public async Task<(MiningFile? File, string? Error)> ExportCallAsync(string userId, string callId, MiningVoices voices, CancellationToken cancellationToken)
    {
        CallJob? job = (await _jobs.GetForUser(userId)).FirstOrDefault(j => j.Id == callId);
        if (job == null)
        {
            return (null, "That call does not exist.");
        }
        if (job.Segments.Count == 0)
        {
            return (null, "That call has no transcript yet.");
        }

        string name = MiningExport.CallFileName(job) + (voices == MiningVoices.Partner && job.HasLearnerSeparation ? "_partner" : string.Empty);
        string srt = MiningExport.Srt(job, voices);
        string work = Path.Combine(Path.GetTempPath(), "akagi-mining-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string? audio = null;
            string? problem = null;
            if (!HasAudio(job))
            {
                problem = "This call has no recording, so only the subtitles are included.";
            }
            else if (!await _clipper.IsAvailableAsync(cancellationToken))
            {
                problem = "ffmpeg was not found on the server, so only the subtitles are included.";
            }
            else
            {
                audio = name + ".mp3";
                try
                {
                    await _clipper.ExtractAsync(job.SourcePath, job.LearnerChannel, voices, Path.Combine(work, audio), cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Could not extract the audio of call {JobId}", job.Id);
                    audio = null;
                    problem = "The audio could not be extracted, so only the subtitles are included.";
                }
            }

            using MemoryStream zip = new();
            using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddText(archive, name + ".srt", srt);
                if (audio != null)
                {
                    archive.CreateEntryFromFile(Path.Combine(work, audio), audio, CompressionLevel.NoCompression);
                }
                if (problem != null)
                {
                    AddText(archive, MiningExport.ReadmeFileName, problem + Environment.NewLine);
                }
            }
            return (new MiningFile(name + ".zip", zip.ToArray()), null);
        }
        finally
        {
            TryDelete(work);
        }
    }

    /// <summary>
    /// The line starting at the time; when several start close by, the one containing the text.
    /// </summary>
    internal static TranscriptSegment? FindSegment(CallJob job, double time, string text)
    {
        string wanted = CallQuotes.Normalize(text);
        List<TranscriptSegment> near = [.. job.Segments.Where(s => Math.Abs(s.Start - time) < 1.5).OrderBy(s => Math.Abs(s.Start - time))];
        return near.FirstOrDefault(s => wanted.Length == 0 || CallQuotes.Normalize(s.Text).Contains(wanted, StringComparison.Ordinal))
            ?? near.FirstOrDefault();
    }

    /// <summary>
    /// Lines from plain transcripts have no end; they end where the speaker's next line starts, at most a few seconds on.
    /// </summary>
    internal static double EndOf(CallJob job, TranscriptSegment segment)
    {
        if (segment.End > segment.Start)
        {
            return segment.End;
        }
        double? next = job.Segments.Where(s => s.Start > segment.Start).Select(s => (double?)s.Start).Min();
        return Math.Min(next ?? segment.Start + 6, segment.Start + 6);
    }

    private static bool HasAudio(CallJob job) => !job.IsTranscript && !string.IsNullOrEmpty(job.SourcePath) && File.Exists(job.SourcePath);

    private static string CallName(CallJob job) =>
        !string.IsNullOrWhiteSpace(job.Partner) ? $"Call with {job.Partner}"
        : !string.IsNullOrWhiteSpace(job.Title) ? job.Title
        : "Call";

    private static void AddText(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes);
    }

    private void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the temporary mining folder {Folder}", folder);
        }
    }
}
