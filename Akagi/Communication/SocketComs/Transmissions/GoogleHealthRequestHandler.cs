using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Connectors.GoogleHealth;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Shows the user's Google Health link on the website and renews it there, so /health is not needed.
/// </summary>
internal class GoogleHealthRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(GoogleHealthRequestTransmission);

    private readonly IGoogleHealthLinking _linking;
    private readonly IGoogleHealthSync _sync;
    private readonly ILogger<GoogleHealthRequestHandler> _logger;

    public GoogleHealthRequestHandler(IGoogleHealthLinking linking, IGoogleHealthSync sync, ILogger<GoogleHealthRequestHandler> logger)
    {
        _linking = linking;
        _sync = sync;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in GoogleHealthRequestHandler");
        GoogleHealthRequestTransmission request = GetTransmission<GoogleHealthRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // Linking and syncing call Google, and the session reads nothing else until this method returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user, request)));
        return Task.CompletedTask;
    }

    internal async Task<GoogleHealthResponseTransmission> ProcessAsync(User user, GoogleHealthRequestTransmission request)
    {
        GoogleHealthResponseTransmission response = new() { RequestId = request.RequestId };
        string userId = user.Id!;
        try
        {
            if (_linking.IsConfigured)
            {
                switch (request.Action)
                {
                    case GoogleHealthAction.Status:
                        break;

                    case GoogleHealthAction.Start:
                        response.AuthorizationUrl = await _linking.StartAsync(userId);
                        break;

                    case GoogleHealthAction.Complete:
                        if (string.IsNullOrWhiteSpace(request.Code))
                        {
                            response.Error = "Paste the address your browser landed on after allowing access.";
                            break;
                        }
                        response.Message = await _linking.CompleteAsync(userId, request.Code);
                        break;

                    case GoogleHealthAction.Sync:
                        GoogleHealthSyncResult? result = await _sync.SyncUserAsync(userId);
                        if (result == null)
                        {
                            response.Error = "Google Health is not linked.";
                        }
                        else
                        {
                            response.Message = result.ToString();
                        }
                        break;

                    default:
                        response.Error = "Unknown Google Health request.";
                        break;
                }
            }
            else if (request.Action != GoogleHealthAction.Status)
            {
                response.Error = "Google Health is not configured on the server (GoogleHealth:ClientId and GoogleHealth:ClientSecret).";
            }
        }
        catch (Exception ex) when (ex is GoogleHealthAuthException or HttpRequestException)
        {
            response.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google Health request {Action} failed for user {UserId}", request.Action, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }

        try
        {
            response.Status = await StatusAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the Google Health link of user {UserId}", userId);
            response.Error ??= "The Google Health status could not be loaded.";
        }
        return response;
    }

    private async Task<GoogleHealthStatus> StatusAsync(string userId)
    {
        if (!_linking.IsConfigured)
        {
            return new GoogleHealthStatus();
        }
        GoogleHealthAccount account = await _linking.GetAccountAsync(userId);
        return new GoogleHealthStatus
        {
            Configured = true,
            Linked = account.IsConnected,
            LinkedAtUtc = account.ConnectedAt,
            ExpiresAtUtc = _linking.ExpiresAtUtc(account),
            LastSyncUtc = account.LastSyncUtc,
            LastError = string.IsNullOrEmpty(account.LastError) ? null : account.LastError
        };
    }
}
