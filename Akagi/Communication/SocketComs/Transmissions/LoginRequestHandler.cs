using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class LoginRequestHandler : SocketTransmissionHandler
{
    private readonly IUserDatabase _userDatabase;
    private readonly ILogger<LoginRequestHandler> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LoginRequestHandler(
        IUserDatabase userDatabase, 
        ILogger<LoginRequestHandler> logger,
        IHttpClientFactory httpClientFactory)
    {
        _userDatabase = userDatabase;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public override string HandlesType => nameof(LoginRequestTransmission);

    public override async Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        LoginRequestTransmission loginRequest = GetTransmission<LoginRequestTransmission>(transmissionWrapper);

        _logger.LogInformation("Received login request for user with token: {Token}", 
            loginRequest.Token?.Length > 20 ? $"{loginRequest.Token[..20]}..." : loginRequest.Token);

        string sanitizedToken = loginRequest.Token?.Trim() ?? string.Empty;
        
        if (sanitizedToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            sanitizedToken = sanitizedToken.Substring(7);
        }
        
        if (string.IsNullOrEmpty(sanitizedToken))
        {
            _logger.LogError("Empty token received");
            throw new ArgumentException("Token cannot be empty");
        }

        try
        {
            // Validate the access token using Google's tokeninfo endpoint
            HttpClient httpClient = _httpClientFactory.CreateClient();
            HttpResponseMessage response = await httpClient.GetAsync($"https://www.googleapis.com/oauth2/v3/tokeninfo?access_token={sanitizedToken}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to validate Google token: {StatusCode}", response.StatusCode);
                throw new ArgumentException("Invalid Google token");
            }

            GoogleTokenInfo? tokenInfo = await response.Content.ReadFromJsonAsync<GoogleTokenInfo>();
            if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Sub))
            {
                _logger.LogError("Invalid token info response");
                throw new ArgumentException("Invalid token info response");
            }

            FilterDefinition<User> userFilter = Builders<User>.Filter.Eq($"{nameof(User.GoogleUser)}.{nameof(User.GoogleUser.Id)}", tokenInfo.Sub);
            User? user = await _userDatabase.GetUser(userFilter);

            if (user == null)
            {
                user = new User
                {
                    Username = tokenInfo.Name ?? "User",
                    Name = tokenInfo.Name ?? "User",
                    LastUsedCommunicator = context.Service.Name,
                    GoogleUser = new GoogleUser
                    {
                        Id = tokenInfo.Sub,
                        Email = tokenInfo.Email!,
                    }
                };
                await _userDatabase.SaveDocumentAsync(user);
                user = await _userDatabase.GetUser(userFilter);
            }

            if (user == null)
            {
                throw new InvalidOperationException("User could not be created or retrieved after login.");
            }
            context.Session.User = user;

            LoginResponseTransmission loginResponse = new();
            context.Session.SendTransmission(loginResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error validating Google token: {Message}", ex.Message);
            throw new ArgumentException("Failed to validate Google token", ex);
        }
    }

    private class GoogleTokenInfo
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }
        
        [JsonPropertyName("email")]
        public string? Email { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("given_name")]
        public string? GivenName { get; set; }
        
        [JsonPropertyName("family_name")]
        public string? FamilyName { get; set; }
    }
}
