using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Akagi.Life.Japanese.Calls;

internal interface ITranscriber
{
    public bool IsConfigured { get; }

    /// <summary>
    /// Transcribes one audio part. Segment times are relative to the start of the part.
    /// </summary>
    public Task<List<TranscriptSegment>> TranscribeAsync(string audioPath, CancellationToken cancellationToken);
}

/// <summary>
/// Speech-to-text through an OpenAI compatible /audio/transcriptions endpoint (OpenAI Whisper, or a local
/// faster-whisper server so recordings never leave the machine).
/// </summary>
internal class OpenAiTranscriber : ITranscriber
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<CallAnalysisOptions> _options;

    public OpenAiTranscriber(HttpClient httpClient, IOptionsMonitor<CallAnalysisOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.CurrentValue.TranscriptionBaseUrl);

    public async Task<List<TranscriptSegment>> TranscribeAsync(string audioPath, CancellationToken cancellationToken)
    {
        CallAnalysisOptions options = _options.CurrentValue;
        using MultipartFormDataContent form = new();
        await using FileStream file = File.OpenRead(audioPath);
        StreamContent audio = new(file);
        audio.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(audioPath).ToLowerInvariant() == ".wav" ? "audio/wav" : "audio/mpeg");
        form.Add(audio, "file", Path.GetFileName(audioPath));
        form.Add(new StringContent(options.TranscriptionModel), "model");
        form.Add(new StringContent(options.Language), "language");
        form.Add(new StringContent("verbose_json"), "response_format");

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(options.TranscriptionBaseUrl), "audio/transcriptions"))
        {
            Content = form
        };
        if (!string.IsNullOrWhiteSpace(options.TranscriptionApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.TranscriptionApiKey);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Transcription failed ({(int)response.StatusCode}): {body}");
        }
        return ParseResponse(body);
    }

    /// <summary>
    /// Reads verbose_json (with segments) or a plain {"text": ...} response.
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
            double duration = root.TryGetProperty("duration", out JsonElement d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
            segments.Add(new TranscriptSegment { Start = 0, End = duration, Text = whole.GetString()!.Trim() });
        }
        return segments;
    }
}
