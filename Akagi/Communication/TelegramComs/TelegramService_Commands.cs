using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Communication.Commands;
using Akagi.Communication.TelegramComs.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    public async Task HandleCommand(Telegram.Bot.Types.Message message, Users.User user)
    {
        try
        {
            if (message.Text != null)
            {
                await HandleTextCommand(message, user);
            }
            else if (message.Document != null || message.Photo != null)
            {
                await HandleDocumentCommand(message, user);
            }
            else
            {
                await _client!.SendMessage(message.Chat.Id, "Unknown command");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process command '{Command}' from user {UserId}", message.Text ?? message.Caption ?? "unkown", user.Id);
            await _client!.SendMessage(message.Chat.Id, "Failed to process command");
        }
        finally
        {
            _logger.LogInformation("Finished processing command '{Command}' from user {UserId}", message.Text ?? message.Caption ?? "unkown", user.Id);
        }
    }

    private async Task HandleTextCommand(Telegram.Bot.Types.Message message, Users.User user)
    {
        if (_client == null || message.Text == null)
        {
            return;
        }

        string command = message.Text;

        TextCommand? textCommand = _textCommands.FirstOrDefault(c => command.StartsWith(c.Name, StringComparison.InvariantCultureIgnoreCase));
        if (textCommand == null)
        {
            await _client.SendMessage(message.Chat.Id, "Unknown command");
            return;
        }
        if (textCommand.AdminOnly && !user.Admin)
        {
            await _client.SendMessage(message.Chat.Id, "This command is for admins only.");
            return;
        }

        string argString = command.Substring(textCommand.Name.Length).Trim();
        string[] args = Command.ParseArguments(argString);

        Character? character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacterId!);
        CharacterGroup? group = await _characterGroupDatabase.GetDocumentByIdAsync(user.TelegramUser!.CurrentGroupId!);
        await using Command.Context context = new()
        {
            Character = character,
            Group = group,
            User = user,
            DatabaseFactory = _databaseFactory
        };

        await using ITelegramCommand.Context telegramContext = new()
        {
            Message = message,
            DatabaseFactory = _databaseFactory
        };
        if (textCommand is ITelegramCommand telegramCommand)
        {
            telegramCommand.Init(telegramContext);
        }

        await textCommand.ExecuteAsync(context, [.. args]);
    }

    private Task HandleDocumentCommand(Telegram.Bot.Types.Message message, Users.User user) => HandleDocumentCommand([message], user);

    private async Task HandleAlbumCommand(IReadOnlyList<Telegram.Bot.Types.Message> messages, Users.User user)
    {
        Telegram.Bot.Types.Message first = messages.First(m => m.Caption != null);
        try
        {
            await HandleDocumentCommand(messages, user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process album command '{Command}' from user {UserId}", first.Caption, user.Id);
            await _client!.SendMessage(first.Chat.Id, "Failed to process command");
        }
    }

    /// <summary>
    /// Runs a document command over one message, or over all files of an album; the caption of the first
    /// captioned message is the command.
    /// </summary>
    private async Task HandleDocumentCommand(IReadOnlyList<Telegram.Bot.Types.Message> messages, Users.User user)
    {
        Telegram.Bot.Types.Message? message = messages.FirstOrDefault(m => m.Caption != null);
        if (_client == null || message?.Caption == null)
        {
            return;
        }

        string command = message.Caption;

        DocumentCommand? documentCommand = _documentCommands.FirstOrDefault(c => command.StartsWith(c.Name, StringComparison.InvariantCultureIgnoreCase));
        if (documentCommand == null)
        {
            await _client.SendMessage(message.Chat.Id, "Unknown command");
            return;
        }
        if (documentCommand.AdminOnly && !user.Admin)
        {
            await _client.SendMessage(message.Chat.Id, "This command is for admins only.");
            return;
        }

        string argString = command.Substring(documentCommand.Name.Length).Trim();
        string[] args = Command.ParseArguments(argString);

        List<TelegramDocument> validFiles = [];
        foreach (Telegram.Bot.Types.Message part in messages)
        {
            if (part.Document != null)
            {
                validFiles.Add(new TelegramDocument(part.Document));
            }
            else if (part.Photo != null && part.Photo.Length > 0)
            {
                validFiles.AddRange(part.Photo.Select(x => new TelegramDocument(x)));
            }
        }
        validFiles.ForEach(x => x.Init(this));

        Character? character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacterId!);
        CharacterGroup? group = await _characterGroupDatabase.GetDocumentByIdAsync(user.TelegramUser!.CurrentGroupId!);
        await using Command.Context context = new()
        {
            Character = character,
            Group = group,
            User = user,
            DatabaseFactory = _databaseFactory
        };

        await using ITelegramCommand.Context telegramContext = new()
        {
            Message = message,
            DatabaseFactory = _databaseFactory
        };
        if (documentCommand is ITelegramCommand telegramCommand)
        {
            telegramCommand.Init(telegramContext);
        }

        await documentCommand.ExecuteAsync(context, [.. validFiles], args);

        validFiles.ForEach(x => x.Dispose());
    }
}
