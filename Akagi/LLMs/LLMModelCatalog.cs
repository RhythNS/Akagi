using Microsoft.Extensions.Logging;

namespace Akagi.LLMs;

/// <summary>
/// A model a provider offers. Prices are in US dollars per million tokens; null when the provider does not say.
/// </summary>
internal record LLMModelInfo(
    ILLM.LLMType Type,
    string Id,
    string Name,
    int ContextLength,
    decimal? PromptPricePerMillion,
    decimal? CompletionPricePerMillion,
    bool SupportsTools);

/// <summary>
/// Lists the models one provider offers. Every <see cref="ILLM.LLMType"/> that can be picked by users needs one.
/// </summary>
internal interface ILLMModelCatalog
{
    public ILLM.LLMType Type { get; }

    public Task<IReadOnlyList<LLMModelInfo>> GetModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The models of all providers, and the providers that could not be asked.
/// </summary>
internal record LLMModelListing(IReadOnlyList<LLMModelInfo> Models, IReadOnlyList<string> Errors)
{
    public LLMModelInfo? Find(ILLM.LLMType type, string id) =>
        Models.FirstOrDefault(m => m.Type == type && string.Equals(m.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Whether the full list of <paramref name="type"/> is known, so a model missing from it really does not exist.
    /// </summary>
    public bool Knows(ILLM.LLMType type) => Models.Any(m => m.Type == type);
}

/// <summary>
/// All catalogs together. Lists are kept for a while: providers change their offer rarely and the list is large.
/// </summary>
internal interface ILLMModelDirectory
{
    public Task<LLMModelListing> GetModelsAsync(CancellationToken cancellationToken = default);
}

internal class LLMModelDirectory : ILLMModelDirectory
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    /// <summary>
    /// A failed provider is asked again after this, not on every request.
    /// </summary>
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private sealed class Entry
    {
        public IReadOnlyList<LLMModelInfo> Models { get; set; } = [];
        public DateTime FetchedUtc { get; set; } = DateTime.MinValue;
        public DateTime RetryUtc { get; set; } = DateTime.MinValue;
        public string? Error { get; set; }
    }

    private readonly IReadOnlyList<ILLMModelCatalog> _catalogs;
    private readonly ILogger<LLMModelDirectory> _logger;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<ILLM.LLMType, Entry> _entries = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LLMModelDirectory(IEnumerable<ILLMModelCatalog> catalogs, ILogger<LLMModelDirectory> logger)
        : this(catalogs, logger, () => DateTime.UtcNow)
    {
    }

    internal LLMModelDirectory(IEnumerable<ILLMModelCatalog> catalogs, ILogger<LLMModelDirectory> logger, Func<DateTime> utcNow)
    {
        _catalogs = [.. catalogs];
        _logger = logger;
        _utcNow = utcNow;
    }

    public async Task<LLMModelListing> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<LLMModelInfo> models = [];
            List<string> errors = [];
            foreach (ILLMModelCatalog catalog in _catalogs)
            {
                Entry entry = await RefreshAsync(catalog, cancellationToken);
                models.AddRange(entry.Models);
                if (entry.Error != null)
                {
                    errors.Add(entry.Error);
                }
            }
            return new LLMModelListing(models, errors);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Entry> RefreshAsync(ILLMModelCatalog catalog, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(catalog.Type, out Entry? entry))
        {
            entry = new Entry();
            _entries[catalog.Type] = entry;
        }

        DateTime now = _utcNow();
        bool fresh = entry.Error == null && now - entry.FetchedUtc < CacheDuration;
        if (fresh || now < entry.RetryUtc)
        {
            return entry;
        }

        try
        {
            // The provider's order is kept; OpenRouter lists new models first
            entry.Models = await catalog.GetModelsAsync(cancellationToken);
            entry.FetchedUtc = now;
            entry.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The last good list stays; it is better than none
            _logger.LogWarning(ex, "Could not list the models of {Type}", catalog.Type);
            entry.RetryUtc = now + RetryAfterFailure;
            entry.Error = $"The models of {catalog.Type} could not be loaded{(entry.Models.Count > 0 ? "; showing an older list" : string.Empty)}.";
        }
        return entry;
    }
}
