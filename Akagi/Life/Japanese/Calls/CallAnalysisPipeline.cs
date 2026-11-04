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
    private readonly ICallConsolidator _consolidator;
    private readonly Func<CallJob, Task> _deliver;
    private readonly CallAnalysisOptions _options;
    private readonly ILogger _logger;

    public CallAnalysisPipeline(ICallJobDatabase jobs,
                                IJapaneseDiagnosisDatabase diagnoses,
                                IAudioSplitter splitter,
                                ITranscriber transcriber,
                                ICallChunkAnalyzer analyzer,
                                ICallConsolidator consolidator,
                                Func<CallJob, Task> deliver,
                                CallAnalysisOptions options,
                                ILogger logger)
    {
        _jobs = jobs;
        _diagnoses = diagnoses;
        _splitter = splitter;
        _transcriber = transcriber;
        _analyzer = analyzer;
        _consolidator = consolidator;
        _deliver = deliver;
        _options = options;
        _logger = logger;
    }

    public string WorkDirectory(CallJob job) => Path.Combine(_options.StorageDirectory, job.Id ?? "unsaved", "parts");

    public async Task RunAsync(CallJob job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Call {JobId}: working on {Title} ({Status}, attempt {Attempt} of {MaxAttempts})",
            job.Id, job.Title, job.Status, job.Attempts + 1, CallJob.MaxAttempts);
        if (job.Status is CallJobStatus.Queued or CallJobStatus.Transcribing)
        {
            await TranscribeAsync(job, cancellationToken);
        }
        if (job.Status == CallJobStatus.Analyzing)
        {
            await AnalyzeAsync(job, cancellationToken);
        }
        if (job.Status == CallJobStatus.Consolidating)
        {
            await ConsolidateAsync(job, cancellationToken);
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
        _logger.LogInformation("Call {JobId}: transcript has {Lines} lines, {Characters} characters, cut into {Chunks} chunks for analysis",
            job.Id, job.Segments.Count, job.Chunks.Sum(c => c.Text.Length), job.Chunks.Count);
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
        _logger.LogInformation("Call {JobId}: split into {Parts} parts of {Minutes} minutes, three passes each; {Done} already transcribed",
            job.Id, count, PartLength.TotalMinutes, job.TranscribedParts);
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
        _logger.LogInformation("Call {JobId}: analyzing {Pending} of {Chunks} chunks", job.Id, job.Chunks.Count(c => !c.Analyzed), job.Chunks.Count);
        foreach (CallChunk chunk in job.Chunks.Where(c => !c.Analyzed).OrderBy(c => c.Index))
        {
            cancellationToken.ThrowIfCancellationRequested();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("Call {JobId}: analyzing chunk {Chunk}/{Chunks} ({Characters} characters)...",
                job.Id, chunk.Index + 1, job.Chunks.Count, chunk.Text.Length);
            // Read fresh for every chunk: a chat may have changed it meanwhile, and the review should see the known issues
            JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(job.UserId);
            ChunkReview review = await _analyzer.AnalyzeAsync(job, chunk, diagnosis, cancellationToken);

            // A finding from an interrupted earlier attempt at this chunk is replaced, not doubled
            job.Findings.RemoveAll(f => f.ChunkIndex == chunk.Index && f.Origin == FindingOrigin.Review);
            foreach (CallFinding finding in review.Findings)
            {
                finding.ChunkIndex = chunk.Index;
                if (finding.IssueId.Length > 0 && diagnosis?.GetIssue(finding.IssueId) == null)
                {
                    finding.IssueId = string.Empty;
                }
                CallQuotes.Check(job, finding);
                CallQuotes.Passes(finding);
                job.Findings.Add(finding);
            }
            chunk.Summary = review.Summary;
            chunk.IssuesRecorded = review.Findings.Count(f => f.Kind == FindingKind.Mistake);
            chunk.Analyzed = true;
            job.Dirty = true;
            await SaveAsync(job);
            _logger.LogInformation("Call {JobId}: analyzed chunk {Chunk}/{Chunks} in {Seconds:0}s, {Findings} finding(s), {Rejected} failed the quote check",
                job.Id, chunk.Index + 1, job.Chunks.Count, watch.Elapsed.TotalSeconds, review.Findings.Count,
                review.Findings.Count(f => f.State == FindingState.Rejected));
        }

        job.Status = CallJobStatus.Consolidating;
        await SaveAsync(job);
    }

    /// <summary>
    /// One request over every finding of the call: merges what is one problem, drops what should not count, and writes
    /// the review. When it gives nothing usable, every finding that passed its quote check is kept as noted.
    /// </summary>
    private async Task ConsolidateAsync(CallJob job, CancellationToken cancellationToken)
    {
        if (job.Review == null)
        {
            JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(job.UserId);
            Consolidation? consolidation = job.Findings.Count == 0 && job.Chunks.All(c => c.Summary.Length == 0)
                ? null
                : await _consolidator.ConsolidateAsync(job, diagnosis, cancellationToken);
            job.Review = ApplyConsolidation(job, consolidation, diagnosis);
            _logger.LogInformation("Call {JobId}: consolidated into {Kept} kept and {Dropped} dropped finding(s){Fallback}",
                job.Id, job.Findings.Count(f => f.State == FindingState.Kept), job.Findings.Count(f => f.State == FindingState.Dropped),
                job.Review.Fallback ? " (kept as noted, the consolidation gave nothing usable)" : string.Empty);
        }
        job.Status = CallJobStatus.Reporting;
        await SaveAsync(job);
    }

    internal static CallReview ApplyConsolidation(CallJob job, Consolidation? consolidation, JapaneseDiagnosis? diagnosis)
    {
        Dictionary<string, CallFinding> open = job.Findings
            .Where(f => f.State == FindingState.Candidate)
            .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

        if (consolidation != null)
        {
            foreach ((string[] ids, string reason) in consolidation.Drops)
            {
                foreach (string id in ids)
                {
                    if (open.Remove(id, out CallFinding? dropped))
                    {
                        dropped.State = FindingState.Dropped;
                        dropped.Note = reason;
                    }
                }
            }

            foreach (FindingGroup group in consolidation.Groups)
            {
                string issueId = diagnosis?.GetIssue(group.IssueId)?.Id ?? string.Empty;
                foreach (string id in group.FindingIds)
                {
                    if (!open.TryGetValue(id, out CallFinding? finding) || finding.Kind != FindingKind.Mistake)
                    {
                        continue;
                    }
                    open.Remove(id);
                    finding.State = FindingState.Kept;
                    finding.IssueId = issueId;
                    if (group.Pattern.Length > 0)
                    {
                        finding.Pattern = group.Pattern;
                    }
                    finding.Category = Receivers.Commands.Life.JapaneseCommand.ParseCategory(group.Category) ?? finding.Category;
                    finding.IssueExplanation = group.Explanation;
                    finding.Severity = group.Severity;
                }
            }
        }

        // What the consolidation did not mention is kept as the chunk review noted it
        foreach (CallFinding finding in open.Values)
        {
            finding.State = FindingState.Kept;
        }

        return new CallReview
        {
            Summary = consolidation?.Summary ?? string.Empty,
            Priorities = consolidation?.Priorities ?? [],
            Fallback = consolidation == null
        };
    }

    private async Task ReportAsync(CallJob job)
    {
        DateTime now = DateTime.UtcNow;
        JapaneseDiagnosis diagnosis = await _diagnoses.GetOrCreate(job.UserId);
        SpeechReport speech = SpeechAnalysis.Analyze(job.LearnerText(), job.Register);

        foreach (CallFinding finding in job.Findings)
        {
            CallDiagnosis.Apply(diagnosis, job, finding, now);
        }

        // Fillers and register of mixed recordings include the friends' speech, so they would distort the trend.
        // Measured once: a retried report must not count the same habits again
        if (job.HasLearnerSeparation && !job.Findings.Any(f => f.Origin == FindingOrigin.Measurement))
        {
            List<(DiagnosisIssue Issue, IssueExample? Example)> measured = [];
            SpeechAnalysis.Apply(diagnosis, speech, job.SourceLabel, now, measured);
            foreach ((DiagnosisIssue issue, IssueExample? example) in measured)
            {
                CallFinding finding = new()
                {
                    Kind = FindingKind.Mistake,
                    Origin = FindingOrigin.Measurement,
                    Category = issue.Category,
                    Pattern = issue.Pattern,
                    Said = example?.Said ?? string.Empty,
                    Explanation = issue.Explanation,
                    Severity = issue.Severity,
                    IssueId = issue.Id,
                    State = FindingState.Kept,
                    Applied = true
                };
                if (example != null)
                {
                    example.FindingId = finding.Id;
                }
                CallQuotes.Check(job, finding);
                job.Findings.Add(finding);
            }
        }
        else if (!job.HasLearnerSeparation)
        {
            foreach (PatternMatch match in speech.Patterns)
            {
                diagnosis.RecordPatternUse(match.Pattern.Key, match.Count, now);
            }
        }
        await _diagnoses.SaveDocumentAsync(diagnosis);

        job.Stats = CallStats.Compute(job);
        job.Report = BuildReport(job, speech, diagnosis, now);
        job.Status = CallJobStatus.Completed;
        job.Error = string.Empty;
        await SaveAsync(job);
        _logger.LogInformation("Call {JobId}: report written, {Issues} issue(s) kept; delivering it", job.Id, job.IssueCount);

        try
        {
            await _deliver(job);
            job.Delivered = true;
            await SaveAsync(job);
            _logger.LogInformation("Call {JobId}: done and delivered", job.Id);
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
        int filtered = job.Findings.Count(f => f.State is FindingState.Dropped or FindingState.Rejected);
        text.AppendLine($"Call analysis: {job.Title} ({job.SourceLabel}), {LifeClock.FormatDuration(duration)}, {job.Chunks.Count} part(s) reviewed, " +
                        $"{job.IssueCount} issue(s) noted" + (filtered > 0 ? $", {filtered} finding(s) filtered out." : "."));

        if (job.Review is { Summary.Length: > 0 } review)
        {
            text.AppendLine("Tutor's review: " + review.Summary);
        }
        if (job.Review is { Priorities.Count: > 0 } ranked)
        {
            text.AppendLine("Priorities:");
            foreach (string priority in ranked.Priorities)
            {
                text.AppendLine("- " + priority);
            }
        }

        List<IGrouping<string, CallFinding>> groups = [.. job.KeptMistakes
            .GroupBy(f => f.IssueId.Length > 0 ? f.IssueId : DiagnosisIssue.Key(f.Category, f.Pattern))
            .OrderByDescending(g => g.Max(f => f.Severity) * g.Count())
            .Take(5)];
        if (groups.Count > 0)
        {
            text.AppendLine("Noticed in this call:");
            foreach (IGrouping<string, CallFinding> group in groups)
            {
                CallFinding example = group.FirstOrDefault(f => f.Said.Length > 0) ?? group.First();
                string fix = string.IsNullOrEmpty(example.Better) ? string.Empty : $" -> '{example.Better}'";
                string quote = example.Said.Length > 0 ? $", e.g. '{example.Said}'{fix}" : string.Empty;
                text.AppendLine($"- {example.Category}: {example.Pattern} ({group.Count()}x){quote}");
            }
        }

        List<CallFinding> correct = [.. job.Findings.Where(f => f.Kind == FindingKind.Correct && f.Counts)];
        if (correct.Count > 0)
        {
            text.AppendLine("Got right: " + string.Join("; ", correct.Take(5).Select(f => $"'{f.Said}' ({diagnosis.GetIssue(f.IssueId)?.Pattern ?? f.IssueId})")));
        }

        text.AppendLine(job.HasLearnerSeparation ? "Measured on the learner's speech:" : "Measured on all speakers (no learner channel or label, so only rough):");
        text.AppendLine(speech.Describe());

        if (job.Stats is CallStats stats)
        {
            if (stats.LearnerShare is double share)
            {
                string response = stats.MedianResponseSeconds is double median ? $", answered after {median.ToString("0.0", CultureInfo.InvariantCulture)}s (median)" : string.Empty;
                text.AppendLine($"Conversation: the learner spoke {share.ToString("P0", CultureInfo.InvariantCulture)} of the time in {stats.LearnerTurns} turns{response}, {stats.LongPauses} long pause(s) mid-turn.");
            }
            List<CallWord> top = [.. stats.Words.Where(w => w.Learner > 0 && w.Class is not WordClass.Interjection).OrderByDescending(w => w.Learner).Take(8)];
            if (top.Count > 0)
            {
                text.AppendLine($"Vocabulary: {stats.LearnerDistinct} different words; most used: {string.Join(", ", top.Select(w => $"{w.Lemma} {w.Learner}x"))}.");
            }
        }

        List<CallFinding> strengths = [.. job.Findings.Where(f => f.Kind == FindingKind.Strength && f.Counts)];
        if (strengths.Count > 0)
        {
            text.AppendLine("Strengths: " + string.Join("; ", strengths.Select(s => s.Pattern).Distinct().Take(3)));
        }

        List<CallFinding> phrases = [.. job.Findings.Where(f => f.Kind == FindingKind.Phrase && f.Counts)];
        if (phrases.Count > 0)
        {
            text.AppendLine("Phrases worth adopting: " + string.Join("; ", phrases.Take(4).Select(p => $"{p.Said} ({p.Better})")));
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
            CallJobStatus.Consolidating => $", merging {job.Findings.Count} finding(s)",
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
