using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Characters;
using Akagi.Communication.Commands;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Communication.SocketComs.Transmissions;

internal abstract class SendMessageRequestHandler<M> : SocketTransmissionHandler where M : IMessageRequest
{
    protected IDatabaseFactory DatabaseFactory { get; init; }
    protected ICharacterDatabase CharacterDatabase { get; init; }
    protected IUserDatabase UserDatabase { get; init; }
    protected ILogger<SendTextMessageRequestHandler> Logger { get; init; }

    public SendMessageRequestHandler(IDatabaseFactory databaseFactory,
                                     ICharacterDatabase characterDatabase,
                                     IUserDatabase userDatabase,
                                     ILogger<SendTextMessageRequestHandler> logger)
    {
        DatabaseFactory = databaseFactory;
        CharacterDatabase = characterDatabase;
        UserDatabase = userDatabase;
        Logger = logger;
    }

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in SendTextMessageRequestHandler");
        M message = GetMessage(transmissionWrapper);
        if (message.CharacterId == null)
        {
            throw new ArgumentException($"CharacterId is invalid in {message.GetType()}");
        }
        string text = message.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            Logger.LogWarning("Received empty message from user {UserId}", user.Id);
            SendResponse(context, message, "Message cannot be empty.");
            return Task.CompletedTask;
        }

        if (text.StartsWith("/", StringComparison.InvariantCultureIgnoreCase))
        {
            return HandleCommand(context, user, message);
        }
        else
        {
            return HandleMessage(context, user, message);
        }
    }

    protected abstract M GetMessage(TransmissionWrapper transmission);

    protected abstract Task TryHandleMessage(Context context, Character character, User user, M message);

    protected abstract Task HandleDocumentCommand(Context context, Character? character, User user, M message, DocumentCommand command, string[] args);

    protected virtual async Task HandleTextCommand(Context context, Character? character, User user, M message, TextCommand command, string[] args)
    {
        await using Command.Context textContext = new()
        {
            Character = character,
            User = user,
            DatabaseFactory = DatabaseFactory
        };

        await command.ExecuteAsync(textContext, args);
    }

    protected abstract void SendResponse(Context context, M message, string? error = null);

    private async Task HandleCommand(Context context, User user, M message)
    {
        string text = message.Text;
        Command? command = context.Service.AvailableCommands.FirstOrDefault(command => command.Name.StartsWith(text, StringComparison.InvariantCultureIgnoreCase));
        if (command == null)
        {
            Logger.LogWarning("Unknown command '{Command}' from user {UserId}", text, user.Id);
            SendResponse(context, message, "Invalid command!");

            return;
        }
        if (command.AdminOnly && !user.Admin)
        {
            Logger.LogWarning("Admin command '{Command}' attempted by non-admin user {UserId}", text, user.Id);
            SendResponse(context, message, "This command is for admins only.");
            return;
        }

        SendResponse(context, message);
        Logger.LogInformation("Received command '{Command}' from user {UserId}", text, user.Id);

        string argString = text.Substring(command.Name.Length).Trim();
        string[] args = Command.ParseArguments(argString);
        Character? character = await GetCharacter(message);

        if (command is TextCommand textCommand)
        {
            await HandleTextCommand(context, character, user, message, textCommand, args);
        }
        else if (command is DocumentCommand documentCommand)
        {
            await HandleDocumentCommand(context, character, user, message, documentCommand, args);
        }
        else
        {
            Logger.LogWarning("Command '{CommandName}' is an unknown command type, cannot execute.", command.Name);
            // SendResponse(context, request, $"Unknown command type: {command.GetType().Name}");
        }
    }

    private async Task HandleMessage(Context context, User user, M message)
    {
        string text = message.Text;
        Character? character = await GetCharacter(message);
        if (character == null || character.UserId != user.Id)
        {
            Logger.LogWarning("Character not found or does not belong to user {UserId}", user.Id);
            SendResponse(context, message, "Character not found or does not belong to you.");
            return;
        }

        SendResponse(context, message);

        if (user.LastUsedCommunicator != context.Service.Name)
        {
            user.LastUsedCommunicator = context.Service.Name;
            await UserDatabase.SaveDocumentAsync(user);
        }

        Logger.LogInformation("Received message '{Text}' from user {UserId} for character {CharacterId}", text, user.Id, character.Id);
        try
        {
            await TryHandleMessage(context, character, user, message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing message '{Text}' from user {UserId} for character {CharacterId}", text, user.Id, character.Id);
        }
    }

    private async Task<Character?> GetCharacter(M message)
    {
        return message.CharacterId == null || string.Equals(message.CharacterId, "0", StringComparison.OrdinalIgnoreCase)
            ? null
            : await CharacterDatabase.GetCharacter(message.CharacterId);
    }
}
