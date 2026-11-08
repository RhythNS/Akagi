using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Life.Japanese.Conjugations;
using Akagi.Life.Japanese.Vocabulary;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// The conjugation practice: loading verbs and progress, recording answers, settings and resetting.
/// </summary>
internal class ConjugationRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(ConjugationRequestTransmission);

    private readonly ConjugationService _service;
    private readonly ILogger<ConjugationRequestHandler> _logger;

    public ConjugationRequestHandler(IVerbCatalog catalog, IConjugationItemDatabase items, IConjugationAttemptDatabase attempts,
        IConjugationPreferencesDatabase preferences, IVocabularyIndex vocabulary, ILogger<ConjugationRequestHandler> logger)
        : this(new ConjugationService(catalog, items, attempts, preferences, vocabulary), logger)
    {
    }

    internal ConjugationRequestHandler(ConjugationService service, ILogger<ConjugationRequestHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in ConjugationRequestHandler");
        ConjugationRequestTransmission request = GetTransmission<ConjugationRequestTransmission>(transmissionWrapper);
        SocketSession session = context.Session;

        // Loading reads every call's words the first time, and the session reads nothing else until this returns
        _ = Task.Run(async () => session.SendTransmission(await ProcessAsync(user.Id!, request)));
        return Task.CompletedTask;
    }

    internal async Task<ConjugationResponseTransmission> ProcessAsync(string userId, ConjugationRequestTransmission request)
    {
        ConjugationResponseTransmission response = new() { RequestId = request.RequestId };
        try
        {
            switch (request.Action)
            {
                case ConjugationAction.Load:
                {
                    ConjugationState state = await _service.LoadAsync(userId, request.UtcOffsetMinutes);
                    response.Settings = state.Settings;
                    response.Verbs = [.. state.Verbs];
                    response.Items = [.. state.Items];
                    response.Days = [.. state.Days];
                    response.Mistakes = [.. state.Mistakes];
                    response.DictionaryLoading = state.DictionaryLoading;
                    break;
                }

                case ConjugationAction.Record:
                    if (request.Verb.Length == 0 || !Enum.IsDefined(request.Form))
                    {
                        response.Error = "That answer is missing its verb or form.";
                        break;
                    }
                    response.Item = await _service.RecordAsync(userId, request.Verb, request.Written, request.Reading, request.Class,
                        request.Form, request.Reverse, request.Answer, request.Correct, request.Millis);
                    break;

                case ConjugationAction.SaveSettings:
                    response.Settings = await _service.SaveSettingsAsync(userId, request.Settings ?? new ConjugationSettingsView());
                    break;

                case ConjugationAction.Reset:
                    await _service.ResetAsync(userId);
                    break;

                default:
                    response.Error = "Unknown conjugation request.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conjugation request {Action} failed for user {UserId}", request.Action, userId);
            response.Error = "Something went wrong. Try again in a moment.";
        }
        return response;
    }
}
