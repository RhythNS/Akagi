using Akagi.Life.Japanese.Calls;
using Akagi.LLMs.OpenRouter;
using System.Text.Json;

namespace Akagi.Tests.LLMs;

/// <summary>
/// What a request cost, and how much a model may think, as OpenRouter reports and takes them.
/// </summary>
public class UsageTests
{
    [Fact]
    public void Response_CarriesCostCachedAndThinkingTokens()
    {
        const string json = """
            {"id":"gen-1","choices":[],"usage":{"prompt_tokens":6000,"completion_tokens":2500,"total_tokens":8500,"cost":0.0153,
             "prompt_tokens_details":{"cached_tokens":4096},"completion_tokens_details":{"reasoning_tokens":1800}}}
            """;

        ResponseUsage usage = JsonSerializer.Deserialize<OpenRouterResponse>(json, OpenRouterClient.PayloadJsonOptions)!.Usage!;

        Assert.Equal((6000, 2500, 4096, 1800, 0.0153), (usage.PromptTokens, usage.CompletionTokens, usage.PromptDetails!.CachedTokens, usage.CompletionDetails!.ReasoningTokens, usage.Cost));
    }

    [Fact]
    public void Payload_AsksForTheReasoningEffort_OnlyWhenSet()
    {
        string withEffort = JsonSerializer.Serialize(new OpenRouterPayload { Model = "m", Reasoning = new OpenRouterPayload.ReasoningOptions { Effort = "low" } }, OpenRouterClient.PayloadJsonOptions);
        string without = JsonSerializer.Serialize(new OpenRouterPayload { Model = "m" }, OpenRouterClient.PayloadJsonOptions);

        Assert.Contains("\"reasoning\":{\"effort\":\"low\"}", withEffort);
        Assert.DoesNotContain("reasoning", without);
    }

    [Theory]
    [InlineData("""{"text":"x","usage":{"type":"duration","seconds":600,"cost":0.0167}}""", 600, 0.0167)]
    [InlineData("""{"text":"x","duration":598.5}""", 598.5, null)]
    public void Transcription_ReportsSecondsAndCost(string body, double seconds, double? cost)
    {
        TranscriptionUsage usage = OpenRouterTranscriber.ReadUsage(body)!;

        Assert.Equal((seconds, cost), (usage.Seconds, usage.Cost));
        Assert.Null(OpenRouterTranscriber.ReadUsage("not json"));
    }

    [Fact]
    public void TranscriptionCost_IsEstimatedOnlyWithoutAReport()
    {
        CallJob job = new();

        CallCosts.AddTranscription(job, new TranscriptionUsage(600, 0.02), 0, 0.10);
        CallCosts.AddTranscription(job, null, 1800, 0.10);

        CallCost cost = Assert.Single(job.Costs);
        Assert.Equal((2, 2400.0, true), (cost.Requests, cost.AudioSeconds, cost.Estimated));
        Assert.Equal(0.02 + 0.05, cost.Cost, 6);
    }
}
