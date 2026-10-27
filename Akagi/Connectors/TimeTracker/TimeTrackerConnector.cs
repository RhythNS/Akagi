using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Akagi.Connectors.TimeTracker;

internal interface ITimeTrackerConnector
{
    public bool IsConfigured { get; }
    public Task<List<Definition>> GetDefinitionsAsync(TimeTrackerUserConfig userConfig);
    public Task<Definition?> GetDefinitionAsync(TimeTrackerUserConfig userConfig, string definitionId);
    public Task<List<Entry>> GetEntriesAsync(TimeTrackerUserConfig userConfig, string? definitionId = null);
    public Task<Entry?> GetEntryAsync(TimeTrackerUserConfig userConfig, string entryId);
    public Task<Definition> CreateDefinitionAsync(TimeTrackerUserConfig userConfig, string name, List<FieldDefinition> fields);
    public Task<Entry> CreateEntryAsync(TimeTrackerUserConfig userConfig, string definitionId, Dictionary<string, object> values, DateTime? createdAt = null);
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
        _url = options.CurrentValue.Url.TrimEnd('/');
        _apiKey = options.CurrentValue.ApiKey;
        _httpClient = httpClientFactory.CreateClient(nameof(TimeTrackerConnector));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_apiKey);

    private Task<T?> GetAsync<T>(string endpoint)
    {
        return SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, $"{_url}{endpoint}"));
    }

    private Task<T?> PostAsync<T>(string endpoint, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"{_url}{endpoint}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        return SendAsync<T>(request);
    }

    private async Task<T?> SendAsync<T>(HttpRequestMessage request)
    {
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

    private static string User(TimeTrackerUserConfig userConfig) => $"googleId={Uri.EscapeDataString(userConfig.GoogleId)}";

    public async Task<List<Definition>> GetDefinitionsAsync(TimeTrackerUserConfig userConfig)
    {
        List<Definition>? definitions = await GetAsync<List<Definition>>($"/TimeTracker/Definitions?{User(userConfig)}");
        return definitions ?? [];
    }

    public async Task<Definition?> GetDefinitionAsync(TimeTrackerUserConfig userConfig, string definitionId)
    {
        return await GetAsync<Definition>($"/TimeTracker/Definitions/{definitionId}?{User(userConfig)}");
    }

    public async Task<List<Entry>> GetEntriesAsync(TimeTrackerUserConfig userConfig, string? definitionId = null)
    {
        string endpoint = $"/TimeTracker/Entries?{User(userConfig)}";
        if (!string.IsNullOrEmpty(definitionId))
        {
            endpoint += $"&definitionId={definitionId}";
        }

        List<Entry>? entries = await GetAsync<List<Entry>>(endpoint);
        return entries ?? [];
    }

    public async Task<Entry?> GetEntryAsync(TimeTrackerUserConfig userConfig, string entryId)
    {
        return await GetAsync<Entry>($"/TimeTracker/Entries/{entryId}?{User(userConfig)}");
    }

    public async Task<Definition> CreateDefinitionAsync(TimeTrackerUserConfig userConfig, string name, List<FieldDefinition> fields)
    {
        Definition? created = await PostAsync<Definition>($"/TimeTracker/Definitions?{User(userConfig)}", new { Name = name, Fields = fields });
        return created ?? throw new Exception("TimeTracker returned no definition.");
    }

    public async Task<Entry> CreateEntryAsync(TimeTrackerUserConfig userConfig, string definitionId, Dictionary<string, object> values, DateTime? createdAt = null)
    {
        Entry? created = await PostAsync<Entry>($"/TimeTracker/Entries?{User(userConfig)}", new
        {
            DefinitionId = definitionId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Values = values
        });
        return created ?? throw new Exception("TimeTracker returned no entry.");
    }
}
