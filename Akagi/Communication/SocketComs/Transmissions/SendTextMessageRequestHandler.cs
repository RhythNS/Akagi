using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Communication.Commands;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

internal class SendTextMessageRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(SendTextMessageRequestTransmission);

    private readonly IDatabaseFactory _databaseFactory;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly IUserDatabase _userDatabase;
    private readonly ILogger<SendTextMessageRequestHandler> _logger;

    public SendTextMessageRequestHandler(IDatabaseFactory databaseFactory,
                                         ICharacterDatabase characterDatabase,
                                         IUserDatabase userDatabase,
                                         ILogger<SendTextMessageRequestHandler> logger)
    {
        _databaseFactory = databaseFactory;
        _characterDatabase = characterDatabase;
        _userDatabase = userDatabase;
        _logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in SendTextMessageRequestHandler");
        SendTextMessageRequestTransmission sendTextMessageRequest = GetTransmission<SendTextMessageRequestTransmission>(transmissionWrapper);
        if (sendTextMessageRequest.CharacterId == null)
        {
            throw new ArgumentException("CharacterId cannot be null in SendTextMessageRequestHandler");
        }
        string text = sendTextMessageRequest.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Received empty text message from user {UserId}", user.Id);
            SendResponse(context, sendTextMessageRequest, "Message cannot be empty.");
            return Task.CompletedTask;
        }

        if (text.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
        {
            return HandleCommand(context, user, sendTextMessageRequest, text);
        }
        else
        {
            return HandleMessage(context, user, sendTextMessageRequest, text);
        }
    }

    private async Task HandleCommand(Context context, User user, SendTextMessageRequestTransmission request, string text)
    {
        Command? command = context.Service.AvailableCommands.FirstOrDefault(command => command.Name.StartsWith(text, StringComparison.InvariantCultureIgnoreCase));
        if (command == null)
        {
            _logger.LogWarning("Unknown command '{Command}' from user {UserId}", text, user.Id);
            SendResponse(context, request, "Invalid command!");

            return;
        }
        if (command.AdminOnly && !user.Admin)
        {
            _logger.LogWarning("Admin command '{Command}' attempted by non-admin user {UserId}", text, user.Id);
            SendResponse(context, request, "This command is for admins only.");
            return;
        }

        SendResponse(context, request);
        _logger.LogInformation("Received command '{Command}' from user {UserId}", text, user.Id);

        string argString = text.Substring(command.Name.Length).Trim();
        string[] args = Command.ParseArguments(argString);
        Character? character = await GetCharacter(request);

        if (command is TextCommand textCommand)
        {
            await using Command.Context textContext = new()
            {
                Character = character,
                User = user,
                DatabaseFactory = _databaseFactory
            };

            await textCommand.ExecuteAsync(textContext, args);
        }
        else
        {
            _logger.LogWarning("Command '{CommandName}' is an unknown command type, cannot execute.", command.Name);
            // SendResponse(context, request, $"Unknown command type: {command.GetType().Name}");
        }
    }

    private async Task HandleMessage(Context context, User user, SendTextMessageRequestTransmission request, string text)
    {
        Character? character = await GetCharacter(request);
        if (character == null || character.UserId != user.Id)
        {
            _logger.LogWarning("Character not found or does not belong to user {UserId}", user.Id);
            SendResponse(context, request, "Character not found or does not belong to you.");
            return;
        }

        SendResponse(context, request);

        if (user.LastUsedCommunicator != context.Service.Name)
        {
            user.LastUsedCommunicator = context.Service.Name;
            await _userDatabase.SaveDocumentAsync(user);
        }

        _logger.LogInformation("Received message '{Text}' from user {UserId} for character {CharacterId}", text, user.Id, character.Id);
        try
        {
            await context.Service.RecieveText(user, character, text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message '{Text}' from user {UserId} for character {CharacterId}", text, user.Id, character.Id);
            // SendResponse(context, request, $"Error processing message: {ex.Message}");
        }
    }

    private async Task<Character?> GetCharacter(SendTextMessageRequestTransmission request)
    {
        return request.CharacterId == null ? null : await _characterDatabase.GetCharacter(request.CharacterId);
    }

    private static void SendResponse(Context context, SendTextMessageRequestTransmission request, string? error = null)
    {
        SendTextMessageResponseTransmission response = new()
        {
            CharacterId = request.CharacterId,
            Text = request.Text,
            Error = error,
        };
        context.Session.SendTransmission(response);
    }
}
