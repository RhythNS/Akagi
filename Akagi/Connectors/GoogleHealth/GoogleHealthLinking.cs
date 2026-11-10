using Microsoft.Extensions.Options;

namespace Akagi.Connectors.GoogleHealth;

/// <summary>
/// Linking steps shared by /health and the website.
/// </summary>
internal interface IGoogleHealthLinking
{
    public bool IsConfigured { get; }

    public Task<GoogleHealthAccount> GetAccountAsync(string userId);

    /// <summary>
    /// Starts a new link and returns Google's consent page. An existing link keeps working until the new one is finished.
    /// </summary>
    public Task<string> StartAsync(string userId);

    /// <summary>
    /// Finishes the link with the code or the address the browser landed on, then syncs once. Returns what happened;
    /// throws <see cref="GoogleHealthAuthException"/> or <see cref="HttpRequestException"/> when linking fails.
    /// </summary>
    public Task<string> CompleteAsync(string userId, string codeOrRedirectUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// When Google is expected to drop the link, or null when it does not run out.
    /// </summary>
    public DateTime? ExpiresAtUtc(GoogleHealthAccount account);
}

internal class GoogleHealthLinking : IGoogleHealthLinking
{
    private readonly IGoogleHealthClient _client;
    private readonly IGoogleHealthAccountDatabase _accounts;
    private readonly IGoogleHealthSync _sync;
    private readonly IOptionsMonitor<GoogleHealthOptions> _options;

    public GoogleHealthLinking(IGoogleHealthClient client, IGoogleHealthAccountDatabase accounts, IGoogleHealthSync sync, IOptionsMonitor<GoogleHealthOptions> options)
    {
        _client = client;
        _accounts = accounts;
        _sync = sync;
        _options = options;
    }

    public bool IsConfigured => _client.IsConfigured;

    public Task<GoogleHealthAccount> GetAccountAsync(string userId) => _accounts.GetOrCreate(userId);

    public async Task<string> StartAsync(string userId)
    {
        GoogleHealthAccount account = await _accounts.GetOrCreate(userId);
        string url = _client.StartAuthorization(account);
        await _accounts.SaveDocumentAsync(account);
        return url;
    }

    public async Task<string> CompleteAsync(string userId, string codeOrRedirectUrl, CancellationToken cancellationToken = default)
    {
        GoogleHealthAccount account = await _accounts.GetOrCreate(userId);
        await _client.CompleteAuthorizationAsync(account, codeOrRedirectUrl, cancellationToken);
        await _accounts.SaveDocumentAsync(account);
        if (account.LastError == GoogleHealthClient.NotLinkedMessage)
        {
            return $"Google access granted, but no data yet. {GoogleHealthClient.NotLinkedMessage}";
        }

        try
        {
            GoogleHealthSyncResult? result = await _sync.SyncUserAsync(userId, cancellationToken);
            return $"Google Health linked. {result}";
        }
        catch (Exception ex) when (ex is GoogleHealthAuthException or HttpRequestException)
        {
            // The link itself worked; the background sync tries again
            return $"Google Health linked, but the first sync failed: {ex.Message}";
        }
    }

    public DateTime? ExpiresAtUtc(GoogleHealthAccount account)
    {
        int days = _options.CurrentValue.LinkLifetimeDays;
        return account.IsConnected && days > 0 && account.ConnectedAt.HasValue ? account.ConnectedAt.Value.AddDays(days) : null;
    }
}
