using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.LLMs;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Shows and changes which model each kind of work runs on for the user.
/// </summary>
internal class LLMSettingsRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(LLMSettingsRequestTransmission);

    private readonly ILLMPreferenceService _preferences;
    private readonly ILLMModelDirectory _models;
    private readonly ILogger<LLMSettingsRequestHandler> _logger;

    public LLMSettingsRequestHandler(ILLMPreferenceService preferences, ILLMModelDirectory models, ILogger<LLMSettingsRequestHandler> logger)
    {
        _preferences = preferences;
        _models = models;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in LLMSettingsRequestHandler");
        LLMSettingsRequestTransmission request = GetTransmission<LLMSettingsRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // Listing the models asks the providers, and the session reads nothing else until this method returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user, request)));
        return Task.CompletedTask;
    }

    internal async Task<LLMSettingsResponseTransmission> ProcessAsync(User user, LLMSettingsRequestTransmission request)
    {
        LLMSettingsResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            switch (request.Action)
            {
                case LLMSettingsAction.Overview:
                    LLMModelListing listing = await _models.GetModelsAsync();
                    response.Models = [.. listing.Models.Select(ToBridge)];
                    response.ModelErrors = [.. listing.Errors];
                    break;

                case LLMSettingsAction.Set:
                    if (!Enum.TryParse(request.Provider, out ILLM.LLMType type) || !Enum.IsDefined(type))
                    {
                        throw new LLMPreferenceException($"Unknown provider {request.Provider}.");
                    }
                    await _preferences.SetAsync(user, ParseUsage(request.Usage), type, request.Model);
                    break;

                case LLMSettingsAction.Reset:
                    await _preferences.ResetAsync(user, ParseUsage(request.Usage));
                    break;

                default:
                    throw new LLMPreferenceException("Unknown model settings request.");
            }
        }
        catch (LLMPreferenceException ex)
        {
            response.Error = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Model settings request {Action} failed for user {UserId}", request.Action, user.Id);
            response.Error = "Something went wrong. Try again in a moment.";
        }

        try
        {
            response.Usages = [.. (await _preferences.GetAsync(user)).Select(ToBridge)];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the model settings of user {UserId}", user.Id);
            response.Error ??= "Your model settings could not be loaded.";
        }
        return response;
    }

    private static ILLM.LLMUsage ParseUsage(string usage) =>
        Enum.TryParse(usage, out ILLM.LLMUsage parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new LLMPreferenceException($"Unknown kind of work {usage}.");

    internal static LLMModel ToBridge(LLMModelInfo model) => new()
    {
        Provider = model.Type.ToString(),
        Id = model.Id,
        Name = model.Name,
        ContextLength = model.ContextLength,
        PromptPrice = (double?)model.PromptPricePerMillion,
        CompletionPrice = (double?)model.CompletionPricePerMillion,
        SupportsTools = model.SupportsTools
    };

    internal static LLMUsageSetting ToBridge(LLMUsagePreference preference) => new()
    {
        Usage = preference.Usage.ToString(),
        Description = preference.Description,
        NeedsTools = preference.NeedsTools,
        Provider = preference.Current?.Type.ToString(),
        Model = preference.Current?.Model,
        DefaultProvider = preference.Default?.Type.ToString(),
        DefaultModel = preference.Default?.Model
    };
}
