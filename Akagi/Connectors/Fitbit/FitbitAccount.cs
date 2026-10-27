using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Connectors.Fitbit;

/// <summary>
/// Configuration section "Fitbit". Register a "Personal" app at dev.fitbit.com with the same redirect URI.
/// </summary>
internal class FitbitOptions
{
    public string ClientId { get; set; } = string.Empty;
    /// <summary>
    /// Only needed for "Server" apps; "Personal" and "Client" apps use PKCE without a secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>
    /// Where Fitbit sends the user after consent. It does not have to load: the user pastes the address back to the bot.
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost:8080/fitbit";
    public string Scopes { get; set; } = "sleep activity heartrate";
    public string AuthorizeUrl { get; set; } = "https://www.fitbit.com/oauth2/authorize";
    public string ApiBaseUrl { get; set; } = "https://api.fitbit.com/";
    public int SyncIntervalMinutes { get; set; } = 120;
    /// <summary>
    /// Shorter interval in the user's local morning, so last night's sleep shows up soon after waking.
    /// </summary>
    public int MorningSyncIntervalMinutes { get; set; } = 20;
    public int DaysToSync { get; set; } = 3;
}

/// <summary>
/// A user's Fitbit link. Fitbit refresh tokens are single use, so every refresh must be saved immediately.
/// </summary>
internal class FitbitAccount : Savable
{
    private string _userId = string.Empty;
    private string _fitbitUserId = string.Empty;
    private string _accessToken = string.Empty;
    private string _refreshToken = string.Empty;
    private DateTime _expiresAtUtc;
    private string _scopes = string.Empty;
    private string _pendingState = string.Empty;
    private string _pendingVerifier = string.Empty;
    private DateTime? _connectedAt;
    private DateTime? _lastSyncUtc;
    private string _lastError = string.Empty;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public string FitbitUserId
    {
        get => _fitbitUserId;
        set => SetProperty(ref _fitbitUserId, value);
    }
    public string AccessToken
    {
        get => _accessToken;
        set => SetProperty(ref _accessToken, value);
    }
    public string RefreshToken
    {
        get => _refreshToken;
        set => SetProperty(ref _refreshToken, value);
    }
    public DateTime ExpiresAtUtc
    {
        get => _expiresAtUtc;
        set => SetProperty(ref _expiresAtUtc, value);
    }
    public string Scopes
    {
        get => _scopes;
        set => SetProperty(ref _scopes, value);
    }
    /// <summary>
    /// OAuth state and PKCE verifier of a link that was started but not finished yet.
    /// </summary>
    public string PendingState
    {
        get => _pendingState;
        set => SetProperty(ref _pendingState, value);
    }
    public string PendingVerifier
    {
        get => _pendingVerifier;
        set => SetProperty(ref _pendingVerifier, value);
    }
    public DateTime? ConnectedAt
    {
        get => _connectedAt;
        set => SetProperty(ref _connectedAt, value);
    }
    public DateTime? LastSyncUtc
    {
        get => _lastSyncUtc;
        set => SetProperty(ref _lastSyncUtc, value);
    }
    public string LastError
    {
        get => _lastError;
        set => SetProperty(ref _lastError, value);
    }

    public bool IsConnected => !string.IsNullOrEmpty(RefreshToken);
}

internal interface IFitbitAccountDatabase : IDatabase<FitbitAccount>
{
    public Task<FitbitAccount?> GetForUser(string userId);
    public Task<FitbitAccount> GetOrCreate(string userId);
    public Task<List<FitbitAccount>> GetConnected();
}

internal class FitbitAccountDatabase : Database<FitbitAccount>, IFitbitAccountDatabase
{
    public override string CollectionName => "fitbit_accounts";

    public FitbitAccountDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is FitbitAccount;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((FitbitAccount)savable);

    public async Task<FitbitAccount?> GetForUser(string userId) =>
        (await GetDocumentsByPredicateAsync(Builders<FitbitAccount>.Filter.Eq(a => a.UserId, userId))).FirstOrDefault();

    public async Task<FitbitAccount> GetOrCreate(string userId)
    {
        FitbitAccount? account = await GetForUser(userId);
        if (account != null)
        {
            return account;
        }
        account = new FitbitAccount { UserId = userId };
        await SaveDocumentAsync(account);
        return account;
    }

    public Task<List<FitbitAccount>> GetConnected() =>
        GetDocumentsByPredicateAsync(Builders<FitbitAccount>.Filter.Gt(a => a.RefreshToken, string.Empty));
}
