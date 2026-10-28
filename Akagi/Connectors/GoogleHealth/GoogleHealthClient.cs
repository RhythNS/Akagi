using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Akagi.Connectors.GoogleHealth;

/// <summary>
/// The link cannot be used as it is; the message tells the user what to do.
/// </summary>
internal class GoogleHealthAuthException : Exception
{
    public GoogleHealthAuthException(string message) : base(message)
    {
    }
}

/// <summary>
/// The user did not grant the scope a data type needs (Google lets users approve only some scopes).
/// </summary>
internal class GoogleHealthMissingScopeException : GoogleHealthAuthException
{
    public GoogleHealthMissingScopeException(string message) : base(message)
    {
    }
}

internal interface IGoogleHealthClient
{
    public bool IsConfigured { get; }

    /// <summary>
    /// Starts the authorization code flow with PKCE and returns the consent page URL.
    /// The pending state is stored on the account, which the caller must save.
    /// </summary>
    public string StartAuthorization(GoogleHealthAccount account);

    /// <summary>
    /// Finishes linking with the code, or with the whole redirect address the user copied from the browser, and
    /// checks that the Google account has a Google Health profile.
    /// </summary>
    public Task CompleteAuthorizationAsync(GoogleHealthAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken);

    /// <summary>
    /// GETs an API path relative to the v4 base, refreshing the access token when needed. <paramref name="persist"/>
    /// runs after every token change.
    /// </summary>
    public Task<string> GetAsync(GoogleHealthAccount account, string path, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken);

    /// <summary>
    /// POSTs a JSON body, for rollUp and dailyRollUp.
    /// </summary>
    public Task<string> PostJsonAsync(GoogleHealthAccount account, string path, string json, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the grant at Google. Failures are ignored; the caller forgets the tokens anyway.
    /// </summary>
    public Task RevokeAsync(GoogleHealthAccount account, CancellationToken cancellationToken);
}

internal class GoogleHealthClient : IGoogleHealthClient
{
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    public const string NotLinkedMessage =
        "Your Google account has no Google Health profile yet. Open the Google Health app, sign in with the same Google account " +
        "(moving your Fitbit account over if asked), then run /health sync.";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<GoogleHealthOptions> _options;

    public GoogleHealthClient(HttpClient httpClient, IOptionsMonitor<GoogleHealthOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.CurrentValue.ClientId) && !string.IsNullOrWhiteSpace(_options.CurrentValue.ClientSecret);

    public string StartAuthorization(GoogleHealthAccount account)
    {
        GoogleHealthOptions options = _options.CurrentValue;
        account.PendingVerifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        account.PendingState = Base64Url(RandomNumberGenerator.GetBytes(16));

        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(account.PendingVerifier)));
        Dictionary<string, string> query = new()
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["scope"] = options.Scopes,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = account.PendingState,
            // A refresh token is only issued with offline access, and again only when consent is shown
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true"
        };
        return options.AuthorizeUrl + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    public async Task CompleteAuthorizationAsync(GoogleHealthAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(account.PendingVerifier))
        {
            throw new GoogleHealthAuthException("No link in progress. Start with /health link.");
        }

        (string code, string? state) = ExtractCode(codeOrRedirectUrl);
        if (state != null && state != account.PendingState)
        {
            throw new GoogleHealthAuthException("That address belongs to another link attempt. Start again with /health link.");
        }

        GoogleHealthOptions options = _options.CurrentValue;
        await RequestTokenAsync(account, new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = account.PendingVerifier,
            ["redirect_uri"] = options.RedirectUri
        }, cancellationToken);
        if (string.IsNullOrEmpty(account.RefreshToken))
        {
            throw new GoogleHealthAuthException("Google did not issue a refresh token. Remove the app's access in your Google account settings and link again.");
        }

        account.PendingState = string.Empty;
        account.PendingVerifier = string.Empty;
        account.ConnectedAt = DateTime.UtcNow;
        account.LastError = string.Empty;

        // Google OAuth accepts any Google account; only the identity call shows whether it has a Google Health profile
        try
        {
            string identity = await GetAsync(account, "users/me/identity", _ => Task.CompletedTask, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(identity);
            if (document.RootElement.TryGetProperty("healthUserId", out JsonElement healthUserId))
            {
                account.HealthUserId = healthUserId.GetString() ?? string.Empty;
            }
        }
        catch (GoogleHealthAuthException ex) when (ex.Message == NotLinkedMessage)
        {
            // Keep the tokens: once the profile exists, syncing works without linking again
            account.LastError = NotLinkedMessage;
        }
        catch (Exception ex) when (ex is HttpRequestException or GoogleHealthMissingScopeException or JsonException)
        {
            // The id is informational; a failing identity call must not undo a working link
        }
    }

    public Task<string> GetAsync(GoogleHealthAccount account, string path, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken) =>
        SendAsync(account, () => new HttpRequestMessage(HttpMethod.Get, ApiUri(path)), persist, cancellationToken);

    public Task<string> PostJsonAsync(GoogleHealthAccount account, string path, string json, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken) =>
        SendAsync(account, () => new HttpRequestMessage(HttpMethod.Post, ApiUri(path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }, persist, cancellationToken);

    public async Task RevokeAsync(GoogleHealthAccount account, CancellationToken cancellationToken)
    {
        string token = string.IsNullOrEmpty(account.RefreshToken) ? account.AccessToken : account.RefreshToken;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, _options.CurrentValue.RevokeUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token })
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Already revoked or offline; the tokens are dropped either way
        }
    }

    private Uri ApiUri(string path) => new(new Uri(_options.CurrentValue.ApiBaseUrl), path.TrimStart('/'));

    private async Task<string> SendAsync(GoogleHealthAccount account, Func<HttpRequestMessage> createRequest, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken)
    {
        if (!account.IsConnected)
        {
            throw new GoogleHealthAuthException("Google Health is not linked. Use /health link.");
        }
        if (account.ExpiresAtUtc - DateTime.UtcNow < RefreshMargin)
        {
            await RefreshAsync(account, persist, cancellationToken);
        }

        using (HttpResponseMessage response = await SendWithTokenAsync(account, createRequest, cancellationToken))
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return await ReadSuccessAsync(response, cancellationToken);
            }
        }

        // Revoked or expired early: refresh once and retry
        await RefreshAsync(account, persist, cancellationToken);
        using HttpResponseMessage retry = await SendWithTokenAsync(account, createRequest, cancellationToken);
        return await ReadSuccessAsync(retry, cancellationToken);
    }

    private Task<HttpResponseMessage> SendWithTokenAsync(GoogleHealthAccount account, Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = createRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task RefreshAsync(GoogleHealthAccount account, Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken)
    {
        GoogleHealthOptions options = _options.CurrentValue;
        try
        {
            await RequestTokenAsync(account, new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = account.RefreshToken
            }, cancellationToken);
        }
        catch (GoogleHealthAuthException)
        {
            // The refresh token is gone for good (revoked, or seven days old for a client in testing); link again
            account.AccessToken = string.Empty;
            account.RefreshToken = string.Empty;
            account.LastError = "The Google Health link expired. Link again with /health link.";
            await persist(account);
            throw;
        }
        await persist(account);
    }

    private async Task RequestTokenAsync(GoogleHealthAccount account, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, _options.CurrentValue.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new GoogleHealthAuthException($"Google refused the authorization: {body}");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google token request failed ({(int)response.StatusCode}): {body}");
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        account.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        // Refresh responses usually leave the refresh token out; it stays valid
        if (root.TryGetProperty("refresh_token", out JsonElement refresh) && !string.IsNullOrEmpty(refresh.GetString()))
        {
            account.RefreshToken = refresh.GetString()!;
        }
        account.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out JsonElement expires) ? expires.GetInt32() : 3600);
        if (root.TryGetProperty("scope", out JsonElement scope))
        {
            account.Scopes = scope.GetString() ?? string.Empty;
        }
    }

    private static async Task<string> ReadSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        string reason = ErrorReason(body);
        if (reason == "ACCOUNT_NOT_LINKED")
        {
            throw new GoogleHealthAuthException(NotLinkedMessage);
        }
        if (reason == "MISSING_OAUTH_SCOPE" || response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new GoogleHealthMissingScopeException($"Google Health did not allow this data ({reason}): {ErrorMessage(body)}");
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GoogleHealthAuthException($"Google rejected the access token: {ErrorMessage(body)}");
        }
        if ((int)response.StatusCode == 429)
        {
            throw new HttpRequestException("Google Health rate limit reached; the next sync will try again.");
        }
        throw new HttpRequestException($"Google Health request failed ({(int)response.StatusCode} {reason}): {ErrorMessage(body)}");
    }

    /// <summary>
    /// The ErrorInfo reason of a Google API error, such as ACCOUNT_NOT_LINKED, or its status as a fallback.
    /// </summary>
    internal static string ErrorReason(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out JsonElement error) || error.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }
            if (error.TryGetProperty("details", out JsonElement details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("reason", out JsonElement reason) && reason.GetString() is { Length: > 0 } text)
                    {
                        return text;
                    }
                }
            }
            return error.TryGetProperty("status", out JsonElement status) ? status.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out JsonElement message))
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
        }
        return body.Length > 300 ? body[..300] : body;
    }

    /// <summary>
    /// Accepts "4/0Ab...", "?code=4/0Ab...&amp;state=xyz" or "https://www.google.com/?state=xyz&amp;code=4/0Ab...&amp;scope=...".
    /// </summary>
    internal static (string Code, string? State) ExtractCode(string input)
    {
        string text = input.Trim();
        int query = text.IndexOf('?');
        if (query < 0 && !text.Contains("code=", StringComparison.Ordinal))
        {
            return (text.Split('#')[0], null);
        }

        string parameters = (query >= 0 ? text[(query + 1)..] : text).Split('#')[0];
        System.Collections.Specialized.NameValueCollection values = HttpUtility.ParseQueryString(parameters);
        if (values["error"] is { Length: > 0 } error)
        {
            throw new GoogleHealthAuthException($"Google reported '{error}'. Start again with /health link and approve access.");
        }
        string code = values["code"] ?? throw new GoogleHealthAuthException("The address has no code. Copy the full address after approving access.");
        return (code, values["state"]);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
