using Akagi.LLMs.OpenRouter;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// One timed unit of speech. MAI-Transcribe returns Japanese character by character, punctuation included.
/// </summary>
internal record TranscriptWord(string Text, double Start, double End);

internal interface ITranscriber
{
    public bool IsConfigured { get; }

    /// <summary>
    /// Transcribes one audio part. Segment times are relative to the start of the part.
    /// </summary>
    public Task<List<TranscriptSegment>> TranscribeAsync(string audioPath, CancellationToken cancellationToken);

    /// <summary>
    /// Transcribes one audio part with word timestamps, relative to the start of the part. Segments alone are too coarse
    /// to merge channels: MAI-Transcribe can return a whole channel as a few segments minutes long.
    /// </summary>
    public Task<List<TranscriptWord>> TranscribeWordsAsync(string audioPath, CancellationToken cancellationToken);
}

/// <summary>
/// Speech-to-text through the /audio/transcriptions endpoint of OpenRouter, with the same credentials as the LLMs.
/// </summary>
internal class OpenRouterTranscriber : ITranscriber
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<CallAnalysisOptions> _options;
    private readonly IOptionsMonitor<OpenRouterClient.Options> _openRouter;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// Tries per request. The provider behind OpenRouter throttles now and then and asks to wait a second.
    /// </summary>
    public const int MaxTries = 6;
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

    public OpenRouterTranscriber(HttpClient httpClient, IOptionsMonitor<CallAnalysisOptions> options, IOptionsMonitor<OpenRouterClient.Options> openRouter)
        : this(httpClient, options, openRouter, Task.Delay)
    {
    }

    internal OpenRouterTranscriber(HttpClient httpClient, IOptionsMonitor<CallAnalysisOptions> options, IOptionsMonitor<OpenRouterClient.Options> openRouter,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClient = httpClient;
        _options = options;
        _openRouter = openRouter;
        _delay = delay;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_openRouter.CurrentValue.ApiKey) && !string.IsNullOrWhiteSpace(_openRouter.CurrentValue.BaseUrl);

    public async Task<List<TranscriptSegment>> TranscribeAsync(string audioPath, CancellationToken cancellationToken) =>
        ParseResponse(await PostAsync(audioPath, wordTimestamps: false, cancellationToken));

    public async Task<List<TranscriptWord>> TranscribeWordsAsync(string audioPath, CancellationToken cancellationToken) =>
        ParseWords(await PostAsync(audioPath, wordTimestamps: true, cancellationToken));

    /// <summary>
    /// Waits and tries again when throttled (429) or when the provider is briefly unavailable (5xx).
    /// </summary>
    private async Task<string> PostAsync(string audioPath, bool wordTimestamps, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            (HttpStatusCode status, TimeSpan? retryAfter, string body) = await SendOnceAsync(audioPath, wordTimestamps, cancellationToken);
            if ((int)status is >= 200 and < 300)
            {
                return body;
            }
            if (!IsTransient(status) || attempt >= MaxTries)
            {
                throw new HttpRequestException($"Transcription failed ({(int)status}): {body}", null, status);
            }
            await _delay(RetryDelay(attempt, retryAfter), cancellationToken);
        }
    }

    internal static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// The provider's Retry-After, but never less than an exponential backoff (2, 4, 8 ... seconds): asking again after
    /// exactly one second tends to hit the same limit.
    /// </summary>
    internal static TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        TimeSpan wait = retryAfter is TimeSpan asked && asked > backoff ? asked : backoff;
        return wait > MaxWait ? MaxWait : wait;
    }

    private async Task<(HttpStatusCode Status, TimeSpan? RetryAfter, string Body)> SendOnceAsync(string audioPath, bool wordTimestamps, CancellationToken cancellationToken)
    {
        CallAnalysisOptions options = _options.CurrentValue;
        OpenRouterClient.Options openRouter = _openRouter.CurrentValue;
        using MultipartFormDataContent form = new(Guid.NewGuid().ToString("N"));
        // OpenRouter refuses what .NET writes by default: a quoted boundary, unquoted field names and a filename* parameter
        form.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value = form.Headers.ContentType.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
        await using FileStream file = File.OpenRead(audioPath);
        StreamContent audio = new(file);
        audio.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(audioPath).ToLowerInvariant() == ".wav" ? "audio/wav" : "audio/mpeg");
        audio.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = $"\"{Path.GetFileName(audioPath)}\"" };
        form.Add(audio);
        AddField(form, "model", options.TranscriptionModel);
        AddField(form, "language", options.Language);
        AddField(form, "response_format", "verbose_json");
        if (wordTimestamps)
        {
            AddField(form, "timestamp_granularities[]", "word");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(openRouter.BaseUrl.TrimEnd('/') + "/"), "audio/transcriptions"))
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openRouter.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
        TimeSpan? retryAfter = retry?.Delta ?? (retry?.Date is DateTimeOffset date ? date - DateTimeOffset.UtcNow : null);
        return (response.StatusCode, retryAfter, body);
    }

    private static void AddField(MultipartFormDataContent form, string name, string value)
    {
        StringContent content = new(value);
        content.Headers.ContentType = null;
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
        form.Add(content);
    }

    /// <summary>
    /// Reads the words of verbose_json. A model without word timestamps still works, with its segments as the words.
    /// </summary>
    internal static List<TranscriptWord> ParseWords(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        List<TranscriptWord> words = [];

        if (root.TryGetProperty("words", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                string text = item.TryGetProperty("word", out JsonElement w) ? w.GetString() ?? string.Empty : string.Empty;
                if (text.Length > 0)
                {
                    words.Add(new TranscriptWord(text, item.GetProperty("start").GetDouble(), item.GetProperty("end").GetDouble()));
                }
            }
        }

        return words.Count > 0 ? words : [.. ParseResponse(json).Select(s => new TranscriptWord(s.Text, s.Start, s.End))];
    }

    /// <summary>
    /// Reads verbose_json (with segments) or a plain {"text": ...} response. Which verbose fields are present
    /// varies by provider, so a missing duration falls back to the billed seconds.
    /// </summary>
    internal static List<TranscriptSegment> ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        List<TranscriptSegment> segments = [];

        if (root.TryGetProperty("segments", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                string text = item.TryGetProperty("text", out JsonElement t) ? t.GetString()?.Trim() ?? string.Empty : string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }
                segments.Add(new TranscriptSegment
                {
                    Start = item.TryGetProperty("start", out JsonElement s) ? s.GetDouble() : 0,
                    End = item.TryGetProperty("end", out JsonElement e) ? e.GetDouble() : 0,
                    Text = text
                });
            }
        }

        if (segments.Count == 0 && root.TryGetProperty("text", out JsonElement whole) && !string.IsNullOrWhiteSpace(whole.GetString()))
        {
            segments.Add(new TranscriptSegment { Start = 0, End = ReadDuration(root), Text = whole.GetString()!.Trim() });
        }
        return segments;
    }

    private static double ReadDuration(JsonElement root)
    {
        if (root.TryGetProperty("duration", out JsonElement duration) && duration.ValueKind == JsonValueKind.Number)
        {
            return duration.GetDouble();
        }
        return root.TryGetProperty("usage", out JsonElement usage)
            && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("seconds", out JsonElement seconds)
            && seconds.ValueKind == JsonValueKind.Number
                ? seconds.GetDouble()
                : 0;
    }
}
