using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Akagi.Connectors.Fitbit;

internal class FitbitAuthException : Exception
{
    public FitbitAuthException(string message) : base(message)
    {
    }
}

internal interface IFitbitClient
{
    public bool IsConfigured { get; }

    /// <summary>
    /// Starts the authorization code flow with PKCE and returns the consent page URL.
    /// The pending state is stored on the account, which the caller must save.
    /// </summary>
    public string StartAuthorization(FitbitAccount account);

    /// <summary>
    /// Finishes linking with the code, or with the whole redirect address the user copied from the browser.
    /// </summary>
    public Task CompleteAuthorizationAsync(FitbitAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken);

    /// <summary>
    /// GETs a Web API path, refreshing the access token when needed. <paramref name="persist"/> runs after every token
    /// change, because Fitbit refresh tokens can only be used once.
    /// </summary>
    public Task<string> GetAsync(FitbitAccount account, string path, Func<FitbitAccount, Task> persist, CancellationToken cancellationToken);
}

internal class FitbitClient : IFitbitClient
{
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<FitbitOptions> _options;

    public FitbitClient(HttpClient httpClient, IOptionsMonitor<FitbitOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.CurrentValue.ClientId);

    public string StartAuthorization(FitbitAccount account)
    {
        FitbitOptions options = _options.CurrentValue;
        account.PendingVerifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        account.PendingState = Base64Url(RandomNumberGenerator.GetBytes(16));

        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(account.PendingVerifier)));
        Dictionary<string, string> query = new()
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["scope"] = options.Scopes,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = account.PendingState,
            ["redirect_uri"] = options.RedirectUri
        };
        return options.AuthorizeUrl + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    public async Task CompleteAuthorizationAsync(FitbitAccount account, string codeOrRedirectUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(account.PendingVerifier))
        {
            throw new FitbitAuthException("No link in progress. Start with /fitbit link.");
        }

        (string code, string? state) = ExtractCode(codeOrRedirectUrl);
        if (state != null && state != account.PendingState)
        {
            throw new FitbitAuthException("That address belongs to another link attempt. Start again with /fitbit link.");
        }

        FitbitOptions options = _options.CurrentValue;
        await RequestTokenAsync(account, new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = account.PendingVerifier,
            ["redirect_uri"] = options.RedirectUri
        }, cancellationToken);

        account.PendingState = string.Empty;
        account.PendingVerifier = string.Empty;
        account.ConnectedAt = DateTime.UtcNow;
        account.LastError = string.Empty;
    }

    public async Task<string> GetAsync(FitbitAccount account, string path, Func<FitbitAccount, Task> persist, CancellationToken cancellationToken)
    {
        if (!account.IsConnected)
        {
            throw new FitbitAuthException("Fitbit is not linked. Use /fitbit link.");
        }
        if (account.ExpiresAtUtc - DateTime.UtcNow < RefreshMargin)
        {
            await RefreshAsync(account, persist, cancellationToken);
        }

        using HttpResponseMessage response = await SendGetAsync(account, path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Revoked or expired early: refresh once and retry
            await RefreshAsync(account, persist, cancellationToken);
            using HttpResponseMessage retry = await SendGetAsync(account, path, cancellationToken);
            return await ReadSuccessAsync(retry, cancellationToken);
        }
        return await ReadSuccessAsync(response, cancellationToken);
    }

    private async Task RefreshAsync(FitbitAccount account, Func<FitbitAccount, Task> persist, CancellationToken cancellationToken)
    {
        try
        {
            await RequestTokenAsync(account, new Dictionary<string, string>
            {
                ["client_id"] = _options.CurrentValue.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = account.RefreshToken
            }, cancellationToken);
        }
        catch (FitbitAuthException)
        {
            // The refresh token is gone for good; the user has to link again
            account.AccessToken = string.Empty;
            account.RefreshToken = string.Empty;
            account.LastError = "The Fitbit link expired. Link again with /fitbit link.";
            await persist(account);
            throw;
        }
        await persist(account);
    }

    private async Task RequestTokenAsync(FitbitAccount account, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        FitbitOptions options = _options.CurrentValue;
        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(new Uri(options.ApiBaseUrl), "oauth2/token"))
        {
            Content = new FormUrlEncodedContent(form)
        };
        if (!string.IsNullOrEmpty(options.ClientSecret))
        {
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new FitbitAuthException($"Fitbit refused the authorization: {body}");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Fitbit token request failed ({(int)response.StatusCode}): {body}");
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        account.AccessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        account.RefreshToken = root.GetProperty("refresh_token").GetString() ?? string.Empty;
        account.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out JsonElement expires) ? expires.GetInt32() : 3600);
        if (root.TryGetProperty("user_id", out JsonElement user))
        {
            account.FitbitUserId = user.GetString() ?? string.Empty;
        }
        if (root.TryGetProperty("scope", out JsonElement scope))
        {
            account.Scopes = scope.GetString() ?? string.Empty;
        }
    }

    private Task<HttpResponseMessage> SendGetAsync(FitbitAccount account, string path, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Get, new Uri(new Uri(_options.CurrentValue.ApiBaseUrl), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en_US"));
        return _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<string> ReadSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new FitbitAuthException($"Fitbit rejected the access token: {body}");
        }
        if ((int)response.StatusCode == 429)
        {
            throw new HttpRequestException("Fitbit rate limit reached; the next sync will try again.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Fitbit request failed ({(int)response.StatusCode}): {body}");
        }
        return body;
    }

    /// <summary>
    /// Accepts "abc123", "?code=abc123&amp;state=xyz" or "http://localhost:8080/fitbit?code=abc123&amp;state=xyz#_=_".
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
        string code = values["code"] ?? throw new FitbitAuthException("The address has no code. Copy the full address after approving access.");
        return (code, values["state"]);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
