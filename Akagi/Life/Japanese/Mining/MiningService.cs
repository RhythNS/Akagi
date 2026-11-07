using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
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
/// Mines sentences from calls. A mined sentence's clip is cut in the background into the call's folder, so the page
/// that follows it can show the progress, and is then downloaded as audio, subtitles or an Anki card. A whole call can
/// also be downloaded as SRT subtitles with its audio for subtitle based mining tools.
/// </summary>
internal class MiningService
{
    public const int MaxSentences = 2000;

    /// <summary>Clips being cut, by sentence id. Shared, since request handlers are made per request.</summary>
    private static readonly Dictionary<string, Task> Running = [];
    /// <summary>How many ffmpeg runs at once.</summary>
    private static readonly SemaphoreSlim Gate = new(2);

    private readonly ICallJobDatabase _jobs;
    private readonly IMinedSentenceDatabase _mined;
    private readonly IAudioClipper _clipper;
    private readonly string _storageDirectory;
    private readonly ILogger _logger;

    public MiningService(ICallJobDatabase jobs, IMinedSentenceDatabase mined, IAudioClipper clipper, string storageDirectory, ILogger logger)
    {
        _jobs = jobs;
        _mined = mined;
        _clipper = clipper;
        _storageDirectory = storageDirectory;
        _logger = logger;
    }

    public async Task<List<MinedSentence>> ListAsync(string userId) =>
        [.. (await _mined.GetForUser(userId)).OrderByDescending(m => m.AddedAt)];

    /// <summary>
    /// The user's sentence. One left waiting, because the bot restarted while cutting it, is started again.
    /// </summary>
    public async Task<MinedSentence?> GetAsync(string userId, string id)
    {
        MinedSentence? sentence = await Owned(userId, id);
        if (sentence is { State: MinedClipState.Queued or MinedClipState.Cutting } && !IsRunning(sentence.Id!))
        {
            _ = StartClip(sentence.Id!);
        }
        return sentence;
    }

    /// <summary>
    /// Mines the line of a call that starts at <paramref name="time"/> and starts cutting its clip.
    /// <paramref name="text"/> may be the whole line or one sentence of it; the clip covers the line. Mining the same
    /// thing twice returns the first one.
    /// </summary>
    public async Task<(MinedSentence? Sentence, string? Error)> AddAsync(string userId, string callId, double time, string text, string target)
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
            return (null, $"You have mined {MaxSentences} sentences, which is the limit. Remove some first.");
        }

        bool learner = job.HasLearnerSeparation && job.IsLearnerLine(segment);
        MinedSentence mined = new()
        {
            UserId = userId,
            CallId = callId,
            CallName = CallName(job),
            Date = job.RecordedAt ?? job.CreatedAt,
            Start = segment.Start,
            End = EndOf(job, segment),
            Speaker = learner ? CallJob.DefaultLearnerLabel : job.PartnerLabel,
            IsLearner = learner,
            Text = sentence,
            Target = target.Trim(),
            AddedAt = DateTime.UtcNow,
            State = HasAudio(job) ? MinedClipState.Queued : MinedClipState.NoAudio
        };
        await _mined.SaveDocumentAsync(mined);
        if (mined.State == MinedClipState.Queued)
        {
            _ = StartClip(mined.Id!);
        }
        return (mined, null);
    }

    /// <summary>
    /// Cuts the clip again after it failed.
    /// </summary>
    public async Task<MinedSentence?> RetryAsync(string userId, string id)
    {
        MinedSentence? sentence = await Owned(userId, id);
        if (sentence is { State: MinedClipState.Failed })
        {
            sentence.State = MinedClipState.Queued;
            sentence.Error = string.Empty;
            await _mined.SaveDocumentAsync(sentence);
            _ = StartClip(sentence.Id!);
        }
        return sentence;
    }

    public async Task<bool> RemoveAsync(string userId, string id)
    {
        MinedSentence? sentence = await Owned(userId, id);
        if (sentence == null)
        {
            return false;
        }
        await _mined.DeleteDocumentByIdAsync(id);
        if (ClipPath(sentence) is string clip && File.Exists(clip))
        {
            TryDeleteFile(clip);
        }
        return true;
    }

    /// <summary>
    /// The clip on its own, an SRT timed to it, or a zip with the clip and a one-card Anki import file.
    /// </summary>
    public async Task<(MiningFile? File, string? Error)> DownloadAsync(string userId, string id, MiningFileKind kind)
    {
        MinedSentence? sentence = await Owned(userId, id);
        if (sentence == null)
        {
            return (null, "That sentence was removed.");
        }
        string name = MiningExport.ClipFileName(sentence);
        string? clip = sentence.State == MinedClipState.Ready && ClipPath(sentence) is string path && File.Exists(path) ? path : null;

        switch (kind)
        {
            case MiningFileKind.Audio:
                return clip == null
                    ? (null, "The clip is not ready.")
                    : (new MiningFile(name, await File.ReadAllBytesAsync(clip)), null);

            case MiningFileKind.Subtitles:
                return (new MiningFile(Path.ChangeExtension(name, ".srt"), new UTF8Encoding(false).GetBytes(MiningExport.SentenceSrt(sentence))), null);

            case MiningFileKind.Anki:
            {
                using MemoryStream zip = new();
                using (ZipArchive archive = new(zip, ZipArchiveMode.Create, leaveOpen: true))
                {
                    // Anki reads the file as UTF-8 only with the byte order mark
                    AddText(archive, MiningExport.CsvFileName, "﻿" + MiningExport.AnkiCsv([(sentence, clip == null ? null : name)]));
                    AddText(archive, MiningExport.ReadmeFileName, MiningExport.Readme(clip == null ? null : name,
                        clip == null ? "There is no audio: the call has no recording, or its clip could not be cut." : null));
                    if (clip != null)
                    {
                        archive.CreateEntryFromFile(clip, name, CompressionLevel.NoCompression);
                    }
                }
                return (new MiningFile(Path.ChangeExtension(name, ".zip"), zip.ToArray()), null);
            }

            default:
                return (null, "Unknown download.");
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
    /// The clip being cut, or a finished task when none is.
    /// </summary>
    internal static Task WaitForClipAsync(string id)
    {
        lock (Running)
        {
            return Running.TryGetValue(id, out Task? task) ? task : Task.CompletedTask;
        }
    }

    private static bool IsRunning(string id)
    {
        lock (Running)
        {
            return Running.ContainsKey(id);
        }
    }

    private Task StartClip(string id)
    {
        lock (Running)
        {
            if (Running.TryGetValue(id, out Task? running))
            {
                return running;
            }
            // The task removes itself under the same lock, so it cannot finish before it is added
            Task task = Task.Run(async () =>
            {
                try
                {
                    await CutAsync(id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cutting the clip of mined sentence {SentenceId} failed", id);
                }
                finally
                {
                    lock (Running)
                    {
                        Running.Remove(id);
                    }
                }
            });
            Running[id] = task;
            return task;
        }
    }

    private async Task CutAsync(string id)
    {
        await Gate.WaitAsync();
        try
        {
            MinedSentence? sentence = await _mined.GetDocumentByIdAsync(id);
            if (sentence == null || sentence.State is MinedClipState.Ready or MinedClipState.NoAudio)
            {
                return;
            }
            CallJob? job = (await _jobs.GetForUser(sentence.UserId)).FirstOrDefault(j => j.Id == sentence.CallId);
            if (job == null || !HasAudio(job))
            {
                sentence.State = MinedClipState.NoAudio;
                await _mined.SaveDocumentAsync(sentence);
                return;
            }
            if (ClipPath(sentence) is not string output)
            {
                await Fail(sentence, "There is no place to store the clip (CallAnalysis:StorageDirectory).");
                return;
            }
            if (!await _clipper.IsAvailableAsync(CancellationToken.None))
            {
                await Fail(sentence, "ffmpeg was not found on the server (CallAnalysis:FfmpegPath).");
                return;
            }

            sentence.State = MinedClipState.Cutting;
            await _mined.SaveDocumentAsync(sentence);

            (double start, double end) = MiningExport.ClipRange(sentence);
            // A sentence comes from one voice, so its clip is cut from that voice's channel
            MiningVoices voices = sentence.IsLearner ? MiningVoices.Learner : MiningVoices.Partner;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await _clipper.ClipAsync(job.SourcePath, start, end, job.LearnerChannel, voices, output, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not cut the clip of mined sentence {SentenceId}", id);
                await Fail(sentence, "ffmpeg could not cut the clip. The bot's log has the details.");
                return;
            }

            if (await _mined.GetDocumentByIdAsync(id) == null)
            {
                // Removed while it was being cut
                TryDeleteFile(output);
                return;
            }
            sentence.State = MinedClipState.Ready;
            sentence.ClipSeconds = end - start;
            await _mined.SaveDocumentAsync(sentence);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task Fail(MinedSentence sentence, string error)
    {
        sentence.State = MinedClipState.Failed;
        sentence.Error = error;
        await _mined.SaveDocumentAsync(sentence);
    }

    private async Task<MinedSentence?> Owned(string userId, string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        MinedSentence? sentence = await _mined.GetDocumentByIdAsync(id);
        return sentence?.UserId == userId ? sentence : null;
    }

    /// <summary>
    /// storage/&lt;call&gt;/mined/&lt;sentence&gt;.mp3, so deleting the call takes its clips along. Null when an id could
    /// lead outside the storage directory.
    /// </summary>
    internal string? ClipPath(MinedSentence sentence)
    {
        if (string.IsNullOrWhiteSpace(_storageDirectory) || sentence.Id is not string id || !IsPlainId(id) || !IsPlainId(sentence.CallId))
        {
            return null;
        }
        return Path.Combine(Path.GetFullPath(_storageDirectory), sentence.CallId, "mined", id + ".mp3");
    }

    private static bool IsPlainId(string id) => id.Length > 0 && id.All(char.IsAsciiLetterOrDigit);

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

    private void TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the clip {File}", file);
        }
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
