using Akagi.LLMs.OpenRouter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akagi.Embeddings;

internal class OpenRouterEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<EmbeddingOptions> _embeddingOptions;
    private readonly IOptionsMonitor<OpenRouterClient.Options> _openRouterOptions;
    private readonly ILogger<OpenRouterEmbeddingService> _logger;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterEmbeddingService(
        HttpClient httpClient,
        IOptionsMonitor<EmbeddingOptions> embeddingOptions,
        IOptionsMonitor<OpenRouterClient.Options> openRouterOptions,
        ILogger<OpenRouterEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _embeddingOptions = embeddingOptions;
        _openRouterOptions = openRouterOptions;
        _logger = logger;
    }

    public int Dimension => _embeddingOptions.CurrentValue.Dimension;

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new float[Dimension];
        }

        float[][] results = await GenerateEmbeddingsBatchAsync([text], ct);
        if (results.Length == 0)
        {
            throw new InvalidOperationException("Failed to generate embedding: empty response received from API.");
        }

        return results[0];
    }

    public async Task<float[][]> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        EmbeddingOptions options = _embeddingOptions.CurrentValue;
        string apiKey = !string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.ApiKey
            : _openRouterOptions.CurrentValue?.ApiKey ?? string.Empty;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Embedding API key is not configured. Please specify 'Embedding:ApiKey' or 'OpenRouter:apikey' in appsettings.json.");
        }

        string baseUrl = options.BaseUrl.TrimEnd('/');
        string endpoint = baseUrl.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/embeddings";

        var payload = new
        {
            model = options.Model,
            input = texts
        };

        string jsonPayload = JsonSerializer.Serialize(payload, _jsonOptions);
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending batch embedding request for {Count} texts using model {Model} to {Endpoint}",
            texts.Count, options.Model, endpoint);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);

        string responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Embedding request failed with status code {StatusCode}: {Response}",
                response.StatusCode, responseContent);
            throw new HttpRequestException(
                $"Embedding API request failed with status code {response.StatusCode}: {responseContent}");
        }

        EmbeddingResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<EmbeddingResponseDto>(responseContent, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize embedding response: {Content}", responseContent);
            throw new InvalidOperationException("Failed to deserialize embedding API response.", ex);
        }

        if (dto == null || dto.Data == null || dto.Data.Count == 0)
        {
            throw new InvalidOperationException("Embedding API returned null or empty data list.");
        }

        // Sort by index to guarantee output order aligns with input texts
        float[][] sortedEmbeddings = dto.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();

        if (sortedEmbeddings.Length != texts.Count)
        {
            _logger.LogWarning("Embedding count mismatch: sent {SentCount} texts, received {ReceivedCount} embeddings.",
                texts.Count, sortedEmbeddings.Length);
        }

        return sortedEmbeddings;
    }

    private class EmbeddingResponseDto
    {
        [JsonPropertyName("data")]
        public List<EmbeddingItemDto> Data { get; set; } = [];

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }

    private class EmbeddingItemDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
