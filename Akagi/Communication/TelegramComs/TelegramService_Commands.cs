using Akagi.Characters;
using Akagi.Communication.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    private async Task HandleCommand(Telegram.Bot.Types.Message message, Users.User user)
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

        TextCommand? textCommand = _textCommands.FirstOrDefault(c => c.Name.StartsWith(command, StringComparison.InvariantCultureIgnoreCase));
        if (textCommand != null)
        {
            string[] args = command.Substring(textCommand.Name.Length)
                                   .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            await textCommand.ExecuteAsync(user, args);
            return;
        }
        else if (command.StartsWith("/redo"))
        {
            if (message.ReplyToMessage == null)
            {
                await _client.SendMessage(message.Chat.Id, "Please reply to a message to redo it");
                return;
            }
            await HandleCommand(message.ReplyToMessage, user);
        }
        else if (command.StartsWith("/changeCharacter"))
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _client.SendMessage(message.Chat.Id, "Please provide a character id");
                return;
            }
            string id = parts[1];
            Character? character = await _characterDatabase.GetCharacter(id);
            if (character == null)
            {
                await _client.SendMessage(message.Chat.Id, "Character not found");
                return;
            }
            user!.TelegramUser!.CurrentCharacter = character.Id;
            await _userDatabase.SaveDocumentAsync(user);
            await _client.SendMessage(message.Chat.Id, $"Current character changed to {character.Card.Name}");
        }
        else
        {
            await _client.SendMessage(message.Chat.Id, "Unknown command");
        }
    }

    private async Task HandleDocumentCommand(Telegram.Bot.Types.Message message, Users.User user)
    {
        if (_client == null || message.Caption == null)
        {
            return;
        }

        string command = message.Caption;

        DocumentCommand? documentCommand = _documentCommands.FirstOrDefault(c => c.Name.StartsWith(command, StringComparison.InvariantCultureIgnoreCase));
        if (documentCommand != null)
        {
            string[] args = command.Substring(documentCommand.Name.Length)
                                   .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<TelegramDocument> validFiles = [];
            if (message.Document != null)
            {
                validFiles.Add(new TelegramDocument(message.Document));
            }
            else if (message.Photo != null && message.Photo.Length > 0)
            {
                validFiles.AddRange(message.Photo.Select(x => new TelegramDocument(x)));
            }

            await documentCommand.ExecuteAsync(user, validFiles.ToArray(), args);
            return;
        }
        else
        {
            await _client.SendMessage(message.Chat.Id, "Unknown command");
        }
    }
}
