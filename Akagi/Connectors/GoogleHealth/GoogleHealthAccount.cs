using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Connectors.GoogleHealth;

/// <summary>
/// Configuration section "GoogleHealth". Create a Google Cloud project, enable the Google Health API, create a
/// "Web application" OAuth client with the same redirect URI, add the read scopes on the Data Access page and your
/// account as a test user.
/// </summary>
internal class GoogleHealthOptions
{
    public const string SleepScope = "https://www.googleapis.com/auth/googlehealth.sleep.readonly";
    public const string ActivityScope = "https://www.googleapis.com/auth/googlehealth.activity_and_fitness.readonly";
    public const string MetricsScope = "https://www.googleapis.com/auth/googlehealth.health_metrics_and_measurements.readonly";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>
    /// Where Google sends the user after consent. It does not have to load: the user pastes the address back to the bot.
    /// </summary>
    public string RedirectUri { get; set; } = "https://www.google.com";
    public string Scopes { get; set; } = $"{SleepScope} {ActivityScope} {MetricsScope}";
    public string AuthorizeUrl { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";
    public string TokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string RevokeUrl { get; set; } = "https://oauth2.googleapis.com/revoke";
    public string ApiBaseUrl { get; set; } = "https://health.googleapis.com/v4/";
    public int SyncIntervalMinutes { get; set; } = 120;
    /// <summary>
    /// Shorter interval in the user's local morning, so last night's sleep shows up soon after waking.
    /// </summary>
    public int MorningSyncIntervalMinutes { get; set; } = 20;
    public int DaysToSync { get; set; } = 3;
    /// <summary>
    /// Exercise sessions are few and come on one page, so they are fetched further back: a weekly report sees them all.
    /// </summary>
    public int ExerciseDaysToSync { get; set; } = 14;
    /// <summary>
    /// How long a link lasts before the user has to link again: seven days while the OAuth client is in "Testing".
    /// Set it to 0 once the client is published, so no renewal is asked for.
    /// </summary>
    public int LinkLifetimeDays { get; set; } = 7;
}

/// <summary>
/// A user's Google Health link. Google keeps the refresh token across refreshes, but an OAuth client in "Testing"
/// publishing status gets refresh tokens that expire after seven days, after which the user has to link again.
/// </summary>
internal class GoogleHealthAccount : Savable
{
    private string _userId = string.Empty;
    private string _healthUserId = string.Empty;
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
    /// <summary>
    /// The user's id in Google Health, from users/me/identity.
    /// </summary>
    public string HealthUserId
    {
        get => _healthUserId;
        set => SetProperty(ref _healthUserId, value);
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
    /// <summary>
    /// The scopes the user granted, space separated. Users may grant only some of the requested ones.
    /// </summary>
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

    /// <summary>
    /// Whether the user granted a scope. Unknown grants count as granted, so the API decides.
    /// </summary>
    public bool HasScope(string scope) =>
        string.IsNullOrWhiteSpace(Scopes) || Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);
}

internal interface IGoogleHealthAccountDatabase : IDatabase<GoogleHealthAccount>
{
    public Task<GoogleHealthAccount?> GetForUser(string userId);
    public Task<GoogleHealthAccount> GetOrCreate(string userId);
    public Task<List<GoogleHealthAccount>> GetConnected();
}

internal class GoogleHealthAccountDatabase : Database<GoogleHealthAccount>, IGoogleHealthAccountDatabase
{
    public override string CollectionName => "google_health_accounts";

    public GoogleHealthAccountDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is GoogleHealthAccount;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((GoogleHealthAccount)savable);

    public async Task<GoogleHealthAccount?> GetForUser(string userId) =>
        (await GetDocumentsByPredicateAsync(Builders<GoogleHealthAccount>.Filter.Eq(a => a.UserId, userId))).FirstOrDefault();

    public async Task<GoogleHealthAccount> GetOrCreate(string userId)
    {
        GoogleHealthAccount? account = await GetForUser(userId);
        if (account != null)
        {
            return account;
        }
        account = new GoogleHealthAccount { UserId = userId };
        await SaveDocumentAsync(account);
        return account;
    }

    public Task<List<GoogleHealthAccount>> GetConnected() =>
        GetDocumentsByPredicateAsync(Builders<GoogleHealthAccount>.Filter.Gt(a => a.RefreshToken, string.Empty));
}
