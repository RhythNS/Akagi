using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Akagi.LLMs.OpenRouter;

/// <summary>
/// The models OpenRouter offers, from its public <c>models</c> endpoint.
/// </summary>
internal class OpenRouterModelCatalog : ILLMModelCatalog
{
    public const string HttpClientName = "OpenRouterModels";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenRouterClient.Options> _options;

    public OpenRouterModelCatalog(IHttpClientFactory httpClientFactory, IOptionsMonitor<OpenRouterClient.Options> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public ILLM.LLMType Type => ILLM.LLMType.OpenRouter;

    public async Task<IReadOnlyList<LLMModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        OpenRouterClient.Options options = _options.CurrentValue;
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "models"));
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.Add("Authorization", $"Bearer {options.ApiKey}");
        }

        HttpClient httpClient = _httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        ModelsResponse? body = await response.Content.ReadFromJsonAsync<ModelsResponse>(cancellationToken);
        return Parse(body);
    }

    internal static IReadOnlyList<LLMModelInfo> Parse(ModelsResponse? body) =>
        [.. (body?.Data ?? [])
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .Select(m => new LLMModelInfo(
                ILLM.LLMType.OpenRouter,
                m.Id!,
                string.IsNullOrWhiteSpace(m.Name) ? m.Id! : m.Name!,
                m.ContextLength ?? 0,
                PerMillion(m.Pricing?.Prompt),
                PerMillion(m.Pricing?.Completion),
                m.SupportedParameters?.Contains("tools", StringComparer.OrdinalIgnoreCase) ?? false))];

    /// <summary>
    /// OpenRouter prices per token as a decimal string; negative means the price depends on the routed model.
    /// </summary>
    private static decimal? PerMillion(string? perToken) =>
        decimal.TryParse(perToken, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) && value >= 0
            ? value * 1_000_000m
            : null;

    internal class ModelsResponse
    {
        [JsonPropertyName("data")]
        public List<Model>? Data { get; set; }
    }

    internal class Model
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("context_length")]
        public int? ContextLength { get; set; }

        [JsonPropertyName("pricing")]
        public Pricing? Pricing { get; set; }

        [JsonPropertyName("supported_parameters")]
        public List<string>? SupportedParameters { get; set; }
    }

    internal class Pricing
    {
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("completion")]
        public string? Completion { get; set; }
    }
}
