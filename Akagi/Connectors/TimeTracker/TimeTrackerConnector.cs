using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Akagi.Connectors.TimeTracker;

internal interface ITimeTrackerConnector
{
    public Task<List<Definition>> GetDefinitionsAsync(TimeTrackerUserConfig userConfig);
    public Task<Definition?> GetDefinitionAsync(TimeTrackerUserConfig userConfig, string definitionId);
    public Task<List<Entry>> GetEntriesAsync(TimeTrackerUserConfig userConfig, string? definitionId = null);
    public Task<Entry?> GetEntryAsync(TimeTrackerUserConfig userConfig, string entryId);
}

internal class TimeTrackerConnector : ITimeTrackerConnector
{
    internal class Options
    {
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }

    private readonly string _url;
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TimeTrackerConnector(IOptionsMonitor<Options> options, IHttpClientFactory httpClientFactory)
    {
        _url = options.CurrentValue.Url;
        _apiKey = options.CurrentValue.ApiKey;
        _httpClient = httpClientFactory.CreateClient(nameof(TimeTrackerConnector));
    }

    private async Task<T?> GetAsync<T>(string endpoint)
    {
        HttpRequestMessage request = new(HttpMethod.Get, $"{_url}{endpoint}");
        request.Headers.Add("X-Api-Key", _apiKey);

        HttpResponseMessage responseMessage = await _httpClient.SendAsync(request);
        if (!responseMessage.IsSuccessStatusCode)
        {
            string errorContent = await responseMessage.Content.ReadAsStringAsync();
            throw new Exception($"Request failed with status code {responseMessage.StatusCode}: {errorContent}");
        }

        string content = await responseMessage.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    public async Task<List<Definition>> GetDefinitionsAsync(TimeTrackerUserConfig userConfig)
    {
        List<Definition>? definitions = await GetAsync<List<Definition>>($"/TimeTracker/Definitions?googleId={userConfig.GoogleId}");
        return definitions ?? [];
    }

    public async Task<Definition?> GetDefinitionAsync(TimeTrackerUserConfig userConfig, string definitionId)
    {
        return await GetAsync<Definition>($"/TimeTracker/Definitions/{definitionId}?googleId={userConfig.GoogleId}");
    }

    public async Task<List<Entry>> GetEntriesAsync(TimeTrackerUserConfig userConfig, string? definitionId = null)
    {
        string endpoint = $"/TimeTracker/Entries?googleId={userConfig.GoogleId}";
        if (!string.IsNullOrEmpty(definitionId))
        {
            endpoint += $"&definitionId={definitionId}";
        }

        List<Entry>? entries = await GetAsync<List<Entry>>(endpoint);
        return entries ?? [];
    }

    public async Task<Entry?> GetEntryAsync(TimeTrackerUserConfig userConfig, string entryId)
    {
        return await GetAsync<Entry>($"/TimeTracker/Entries/{entryId}?googleId={userConfig.GoogleId}");
    }
}