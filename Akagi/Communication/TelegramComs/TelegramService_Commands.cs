﻿using Akagi.Characters;
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
        List<string> args = [];
        int i = 0;
        while (i < argString.Length)
        {
            if (argString[i] == '{' && i + 1 < argString.Length && argString[i + 1] == '{')
            {
                int end = argString.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end == -1)
                {
                    args.Add(argString.Substring(i + 2).Trim());
                    break;
                }
                args.Add(argString.Substring(i + 2, end - (i + 2)).Trim());
                i = end + 2;
            }
            else if (!char.IsWhiteSpace(argString[i]))
            {
                int start = i;
                while (i < argString.Length && !char.IsWhiteSpace(argString[i]))
                {
                    i++;
                }
                args.Add(argString.Substring(start, i - start));
            }
            else
            {
                i++;
            }
        }

        Character? character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacterId!);
        await using Command.Context context = new()
        {
            Character = character,
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

    private async Task HandleDocumentCommand(Telegram.Bot.Types.Message message, Users.User user)
    {
        if (_client == null || message.Caption == null)
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
        validFiles.ForEach(x => x.Init(this));

        Character? character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacterId!);
        await using Command.Context context = new()
        {
            Character = character,
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
