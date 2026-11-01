using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.Life.Japanese.Calls;
using Akagi.LLMs.OpenRouter;
using Akagi.Tests.Life;
using Akagi.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using System.Text;

namespace Akagi.Tests.Live;

/// <summary>
/// Paid: costs about $0.05 for ten minutes of audio. Runs only when AKAGI_LIVE_OPENROUTER_KEY holds a key,
/// AKAGI_LIVE_CALL a stereo recording with the learner on the left, and AKAGI_LIVE_FFMPEG the path to ffmpeg.
/// AKAGI_LIVE_OUTPUT names a folder for the transcript and the review instruction.
/// </summary>
public sealed class LiveTranscriptionFactAttribute : FactAttribute
{
    public LiveTranscriptionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AKAGI_LIVE_OPENROUTER_KEY")))
        {
            Skip = "Set AKAGI_LIVE_OPENROUTER_KEY, AKAGI_LIVE_CALL and AKAGI_LIVE_FFMPEG to run the paid transcription test.";
        }
    }
}

public class LiveTranscriptionTests
{
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class NoAnalysis : ICallChunkAnalyzer
    {
        public Task<int> AnalyzeAsync(CallJob job, CallChunk chunk, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    [LiveTranscriptionFact]
    public async Task Call_BecomesALabeledConversationThroughTheRealModel()
    {
        string storage = Path.Combine(Path.GetTempPath(), "akagi-live-" + Guid.NewGuid().ToString("N")[..8]);
        CallAnalysisOptions options = new() { StorageDirectory = storage };
        OpenRouterClient.Options openRouter = new()
        {
            ApiKey = Environment.GetEnvironmentVariable("AKAGI_LIVE_OPENROUTER_KEY")!,
            BaseUrl = "https://openrouter.ai/api/v1/",
            ResponsePath = "chat/completions"
        };
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
        InMemoryCallJobDatabase jobs = new();
        InMemoryJapaneseDiagnosisDatabase diagnoses = new();
        CallAnalysisPipeline pipeline = new(jobs, diagnoses,
            new AudioSplitter(Environment.GetEnvironmentVariable("AKAGI_LIVE_FFMPEG")!),
            new OpenRouterTranscriber(http, new Monitor<CallAnalysisOptions>(options), new Monitor<OpenRouterClient.Options>(openRouter)),
            new NoAnalysis(), _ => Task.CompletedTask, options, NullLogger.Instance);

        CallJob job = new()
        {
            UserId = ObjectId.GenerateNewId().ToString(),
            Title = "Lee",
            Partner = "Lee",
            SourcePath = Environment.GetEnvironmentVariable("AKAGI_LIVE_CALL")!,
            LearnerChannel = LearnerChannel.Left
        };
        await jobs.SaveDocumentAsync(job);

        try
        {
            await pipeline.RunAsync(job, CancellationToken.None);

            Assert.Equal(CallJobStatus.Completed, job.Status);
            Assert.True(job.HasBothVoices);
            Assert.Contains(job.Segments, s => s.Speaker == "Me");
            Assert.Contains(job.Segments, s => s.Speaker == "Lee");
            Assert.Equal(job.Segments.OrderBy(s => s.Start).Select(s => s.Start), job.Segments.Select(s => s.Start));

            string? output = Environment.GetEnvironmentVariable("AKAGI_LIVE_OUTPUT");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "transcript.txt"), string.Join('\n', job.Segments.Select(TranscriptChunker.Format)), Encoding.UTF8);
                await File.WriteAllTextAsync(Path.Combine(output, "instruction.txt"), ToolCallChunkAnalyzer.BuildInstruction(job, null, DateTime.UtcNow), Encoding.UTF8);
                await File.WriteAllTextAsync(Path.Combine(output, "learner.txt"), job.LearnerText(), Encoding.UTF8);
                await File.WriteAllTextAsync(Path.Combine(output, "report.txt"), job.Report, Encoding.UTF8);
            }
        }
        finally
        {
            Directory.Delete(storage, recursive: true);
        }
    }
}
