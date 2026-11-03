using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// Turns a long recording into diagnosis updates and a short report: split into parts, transcribe part by part,
/// cut the transcript into chunks, let the tutor analyze each chunk, then measure the learner's speech locally and
/// summarize. Every step saves its progress on the job, so a crash or restart never repeats paid work.
/// </summary>
internal class CallAnalysisPipeline
{
    private readonly ICallJobDatabase _jobs;
    private readonly IJapaneseDiagnosisDatabase _diagnoses;
    private readonly IAudioSplitter _splitter;
    private readonly ITranscriber _transcriber;
    private readonly ICallChunkAnalyzer _analyzer;
    private readonly Func<CallJob, Task> _deliver;
    private readonly CallAnalysisOptions _options;
    private readonly ILogger _logger;

    public CallAnalysisPipeline(ICallJobDatabase jobs,
                                IJapaneseDiagnosisDatabase diagnoses,
                                IAudioSplitter splitter,
                                ITranscriber transcriber,
                                ICallChunkAnalyzer analyzer,
                                Func<CallJob, Task> deliver,
                                CallAnalysisOptions options,
                                ILogger logger)
    {
        _jobs = jobs;
        _diagnoses = diagnoses;
        _splitter = splitter;
        _transcriber = transcriber;
        _analyzer = analyzer;
        _deliver = deliver;
        _options = options;
        _logger = logger;
    }

    public string WorkDirectory(CallJob job) => Path.Combine(_options.StorageDirectory, job.Id ?? "unsaved", "parts");

    public async Task RunAsync(CallJob job, CancellationToken cancellationToken)
    {
        if (job.Status is CallJobStatus.Queued or CallJobStatus.Transcribing)
        {
            await TranscribeAsync(job, cancellationToken);
        }
        if (job.Status == CallJobStatus.Analyzing)
        {
            await AnalyzeAsync(job, cancellationToken);
        }
        if (job.Status == CallJobStatus.Reporting)
        {
            await ReportAsync(job);
        }
    }

    /// <summary>
    /// Records a failed attempt; after <see cref="CallJob.MaxAttempts"/> the job is given up.
    /// </summary>
    public async Task FailAttemptAsync(CallJob job, Exception exception)
    {
        job.Attempts++;
        job.Error = exception.Message;
        if (job.Attempts >= CallJob.MaxAttempts)
        {
            job.Status = CallJobStatus.Failed;
        }
        await SaveAsync(job);
    }

    private async Task TranscribeAsync(CallJob job, CancellationToken cancellationToken)
    {
        job.Status = CallJobStatus.Transcribing;
        if (job.IsTranscript)
        {
            string content = await File.ReadAllTextAsync(job.SourcePath, cancellationToken);
            job.Segments = TranscriptParser.Parse(content, job.SourcePath);
        }
        else
        {
            if (!_transcriber.IsConfigured)
            {
                throw new InvalidOperationException("Transcription needs the OpenRouter credentials (OpenRouter:apikey and OpenRouter:baseUrl).");
            }

            if (job.LearnerChannel == LearnerChannel.Mixed)
            {
                await TranscribeMixedAsync(job, cancellationToken);
            }
            else
            {
                await TranscribeChannelsAsync(job, cancellationToken);
            }
        }

        if (job.Segments.Count == 0)
        {
            job.Status = CallJobStatus.Failed;
            job.Error = "No speech found in the recording.";
            await SaveAsync(job);
            return;
        }

        job.Chunks = TranscriptChunker.Chunk(job.Segments, Math.Max(500, _options.ChunkCharacters));
        job.Status = CallJobStatus.Analyzing;
        await SaveAsync(job);
    }

    private TimeSpan PartLength => TimeSpan.FromMinutes(Math.Max(1, _options.PartMinutes));

    private async Task TranscribeMixedAsync(CallJob job, CancellationToken cancellationToken)
    {
        IReadOnlyList<AudioPart> parts = await _splitter.SplitAsync(job.SourcePath, PartLength, job.LearnerChannel, WorkDirectory(job), cancellationToken);
        for (int i = job.TranscribedParts; i < parts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<TranscriptSegment> segments = await _transcriber.TranscribeAsync(parts[i].Path, cancellationToken);
            await AddPartAsync(job, segments, parts[i].OffsetSeconds, i, parts.Count);
        }
    }

    /// <summary>
    /// Three passes per part: each voice alone, and both together as a second hearing. Hearing the partner is what lets
    /// the review see what the learner was answering, and whether they understood it.
    /// </summary>
    private async Task TranscribeChannelsAsync(CallJob job, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.LearnerSpeaker))
        {
            job.LearnerSpeaker = CallJob.DefaultLearnerLabel;
        }
        LearnerChannel partnerChannel = job.LearnerChannel == LearnerChannel.Left ? LearnerChannel.Right : LearnerChannel.Left;
        string work = WorkDirectory(job);
        IReadOnlyList<AudioPart> learnerParts = await _splitter.SplitAsync(job.SourcePath, PartLength, job.LearnerChannel, Path.Combine(work, "learner"), cancellationToken);
        IReadOnlyList<AudioPart> partnerParts = await _splitter.SplitAsync(job.SourcePath, PartLength, partnerChannel, Path.Combine(work, "partner"), cancellationToken);
        IReadOnlyList<AudioPart> mixParts = await _splitter.SplitBalancedAsync(job.SourcePath, PartLength, Path.Combine(work, "mix"), cancellationToken);

        int count = Math.Min(learnerParts.Count, Math.Min(partnerParts.Count, mixParts.Count));
        for (int i = job.TranscribedParts; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ChannelTranscript learner = await TranscribeChannelAsync(job, "learner", i, learnerParts[i], cancellationToken);
            ChannelTranscript partner = await TranscribeChannelAsync(job, "partner", i, partnerParts[i], cancellationToken);
            List<TranscriptWord> mix = await TranscribeWordsCachedAsync(job, "mix", i, mixParts[i], cancellationToken);

            List<TranscriptSegment> segments = ConversationMerger.Merge(learner, partner, mix, job.LearnerSpeaker, job.PartnerLabel);
            await AddPartAsync(job, segments, learnerParts[i].OffsetSeconds, i, count);
        }
    }

    private async Task<ChannelTranscript> TranscribeChannelAsync(CallJob job, string pass, int index, AudioPart part, CancellationToken cancellationToken) =>
        new(await TranscribeWordsCachedAsync(job, pass, index, part, cancellationToken), await _splitter.MeasureLoudnessAsync(part.Path, cancellationToken));

    /// <summary>
    /// A part takes three paid passes. Each finished pass is kept on disk, so a failure in a later pass of the same part
    /// does not pay for the earlier ones again.
    /// </summary>
    private async Task<List<TranscriptWord>> TranscribeWordsCachedAsync(CallJob job, string pass, int index, AudioPart part, CancellationToken cancellationToken)
    {
        string cachePath = Path.Combine(_options.StorageDirectory, job.Id ?? "unsaved", "transcripts", $"{pass}-{index}.json");
        if (File.Exists(cachePath))
        {
            try
            {
                List<TranscriptWord>? cached = JsonSerializer.Deserialize<List<TranscriptWord>>(await File.ReadAllTextAsync(cachePath, cancellationToken));
                if (cached != null)
                {
                    return cached;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Call {JobId}: ignoring unreadable transcript {Path}", job.Id, cachePath);
            }
        }

        List<TranscriptWord> words = await _transcriber.TranscribeWordsAsync(part.Path, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(words), cancellationToken);
        return words;
    }

    private async Task AddPartAsync(CallJob job, List<TranscriptSegment> segments, double offsetSeconds, int index, int count)
    {
        foreach (TranscriptSegment segment in segments)
        {
            segment.Start += offsetSeconds;
            segment.End += offsetSeconds;
        }
        job.Segments = [.. job.Segments, .. segments];
        job.TranscribedParts = index + 1;
        await SaveAsync(job);
        _logger.LogInformation("Call {JobId}: transcribed part {Part}/{Parts}", job.Id, index + 1, count);
    }

    private async Task AnalyzeAsync(CallJob job, CancellationToken cancellationToken)
    {
        foreach (CallChunk chunk in job.Chunks.Where(c => !c.Analyzed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunk.IssuesRecorded = await _analyzer.AnalyzeAsync(job, chunk, cancellationToken);
            chunk.Analyzed = true;
            job.Dirty = true;
            await SaveAsync(job);
            _logger.LogInformation("Call {JobId}: analyzed chunk {Chunk}/{Chunks}, {Issues} issue(s)", job.Id, chunk.Index + 1, job.Chunks.Count, chunk.IssuesRecorded);
        }

        job.Status = CallJobStatus.Reporting;
        await SaveAsync(job);
    }

    private async Task ReportAsync(CallJob job)
    {
        DateTime now = DateTime.UtcNow;
        JapaneseDiagnosis diagnosis = await _diagnoses.GetOrCreate(job.UserId);
        SpeechReport speech = SpeechAnalysis.Analyze(job.LearnerText(), job.Register);

        // Fillers and register of mixed recordings include the friends' speech, so they would distort the trend
        if (job.HasLearnerSeparation)
        {
            SpeechAnalysis.Apply(diagnosis, speech, job.SourceLabel, now);
        }
        else
        {
            foreach (PatternMatch match in speech.Patterns)
            {
                diagnosis.RecordPatternUse(match.Pattern.Key, match.Count, now);
            }
        }
        await _diagnoses.SaveDocumentAsync(diagnosis);

        job.Report = BuildReport(job, speech, diagnosis, now);
        job.Status = CallJobStatus.Completed;
        job.Error = string.Empty;
        await SaveAsync(job);

        try
        {
            await _deliver(job);
            job.Delivered = true;
            await SaveAsync(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not deliver the analysis of call {JobId}", job.Id);
        }
    }

    internal static string BuildReport(CallJob job, SpeechReport speech, JapaneseDiagnosis diagnosis, DateTime nowUtc)
    {
        StringBuilder text = new();
        TimeSpan duration = TimeSpan.FromSeconds(job.DurationSeconds);
        text.AppendLine($"Call analysis: {job.Title} ({job.SourceLabel}), {LifeClock.FormatDuration(duration)}, {job.Chunks.Count} part(s) reviewed, " +
                        $"{job.Chunks.Sum(c => c.IssuesRecorded)} issue(s) noted.");

        List<(DiagnosisIssue Issue, int Count)> fromCall = [.. diagnosis.Issues
            .Select(i => (Issue: i, Count: i.Examples.Count(e => e.Source == job.SourceLabel)))
            .Where(i => i.Count > 0)
            .OrderByDescending(i => i.Issue.Severity * i.Count)
            .Take(5)];
        if (fromCall.Count > 0)
        {
            text.AppendLine("Noticed in this call:");
            foreach ((DiagnosisIssue issue, int count) in fromCall)
            {
                IssueExample example = issue.Examples.Last(e => e.Source == job.SourceLabel);
                string fix = string.IsNullOrEmpty(example.Better) ? string.Empty : $" -> '{example.Better}'";
                text.AppendLine($"- {issue.Category}: {issue.Pattern} ({count}x), e.g. '{example.Said}'{fix}");
            }
        }

        text.AppendLine(job.HasLearnerSeparation ? "Measured on the learner's speech:" : "Measured on all speakers (no learner channel or label, so only rough):");
        text.AppendLine(speech.Describe());

        List<DiagnosisStrength> strengths = [.. diagnosis.Strengths.Where(s => s.LastSeen >= job.CreatedAt).Take(3)];
        if (strengths.Count > 0)
        {
            text.AppendLine("Strengths: " + string.Join("; ", strengths.Select(s => s.Text)));
        }

        List<DiagnosisIssue> focus = diagnosis.Focus(3, nowUtc);
        if (focus.Count > 0)
        {
            text.Append("Overall focus now: " + string.Join(", ", focus.Select(f => f.Pattern)));
        }
        return text.ToString().TrimEnd();
    }

    internal static string DescribeProgress(CallJob job)
    {
        string progress = job.Status switch
        {
            CallJobStatus.Transcribing when !job.IsTranscript => $", {job.TranscribedParts} part(s) transcribed",
            CallJobStatus.Analyzing => $", {job.Chunks.Count(c => c.Analyzed)}/{job.Chunks.Count} chunks analyzed",
            _ => string.Empty
        };
        string error = string.IsNullOrEmpty(job.Error) ? string.Empty : $" ({job.Error})";
        string created = job.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"[{job.ShortId}] {job.Title}: {job.Status.ToString().ToLowerInvariant()}{progress}, added {created} UTC{error}";
    }

    private async Task SaveAsync(CallJob job)
    {
        job.Touch(DateTime.UtcNow);
        await _jobs.SaveDocumentAsync(job);
    }
}
