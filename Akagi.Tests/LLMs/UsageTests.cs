using Akagi.Life.Japanese.Calls;
using Akagi.LLMs.OpenRouter;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
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

    private const string Ok = """{"id":"gen","choices":[{"message":{"role":"assistant","content":"hi"}}]}""";
    private const string ThrottledInside = """{"id":"gen","choices":[{"error":{"code":429,"message":"rate-limited upstream"}}]}""";

    private static (Func<Task<(HttpStatusCode, TimeSpan?, string)>> Send, List<int> Calls) Script(params (HttpStatusCode Status, string Body)[] answers)
    {
        List<int> calls = [];
        return (() =>
        {
            calls.Add(calls.Count);
            (HttpStatusCode status, string body) = answers[Math.Min(calls.Count - 1, answers.Length - 1)];
            return Task.FromResult<(HttpStatusCode, TimeSpan?, string)>((status, status == HttpStatusCode.TooManyRequests ? TimeSpan.FromSeconds(1) : null, body));
        }, calls);
    }

    [Fact]
    public async Task Requests_TryAgainWhenThrottled_InTheStatusOrInsideTheResponse()
    {
        List<TimeSpan> waits = [];
        (var send, List<int> calls) = Script((HttpStatusCode.TooManyRequests, "{}"), (HttpStatusCode.OK, ThrottledInside), (HttpStatusCode.OK, Ok));

        OpenRouterResponse response = await OpenRouterClient.SendWithRetryAsync(send, w => { waits.Add(w); return Task.CompletedTask; }, NullLogger.Instance, "m");

        Assert.Equal(3, calls.Count);
        Assert.Equal("gen", response.Id);
        // The server's Retry-After first, then the backoff
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)], waits);
    }

    [Fact]
    public async Task Requests_GiveUpAfterTheLastTry_AndDoNotRetryRealErrors()
    {
        (var throttled, List<int> tries) = Script((HttpStatusCode.ServiceUnavailable, "down"));
        Exception gaveUp = await Assert.ThrowsAsync<Exception>(() => OpenRouterClient.SendWithRetryAsync(throttled, _ => Task.CompletedTask, NullLogger.Instance, "m"));
        Assert.Equal(OpenRouterClient.MaxTries, tries.Count);
        Assert.Contains("ServiceUnavailable", gaveUp.Message);

        (var invalid, List<int> once) = Script((HttpStatusCode.BadRequest, "bad tool schema"));
        await Assert.ThrowsAsync<Exception>(() => OpenRouterClient.SendWithRetryAsync(invalid, _ => Task.CompletedTask, NullLogger.Instance, "m"));
        Assert.Single(once);

        (var errorInside, List<int> single) = Script((HttpStatusCode.OK, """{"error":{"code":401,"message":"No auth"}}"""));
        Exception unauthorized = await Assert.ThrowsAsync<Exception>(() => OpenRouterClient.SendWithRetryAsync(errorInside, _ => Task.CompletedTask, NullLogger.Instance, "m"));
        Assert.Single(single);
        Assert.Contains("No auth", unauthorized.Message);
    }

    [Fact]
    public void RetryDelay_IsCapped()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), OpenRouterClient.RetryDelay(1, null));
        Assert.Equal(OpenRouterClient.MaxRetryWait, OpenRouterClient.RetryDelay(1, TimeSpan.FromMinutes(5)));
        Assert.True(OpenRouterClient.IsRetriable(429) && OpenRouterClient.IsRetriable(503) && !OpenRouterClient.IsRetriable(400));
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
