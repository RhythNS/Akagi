﻿using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    public override Task SendMessage(Users.User user, Character character, Characters.Conversations.Message message)
    {
        if (message is TextMessage textMessage)
        {
            return SendMessage(user, character, textMessage.Text);
        }
        else
        {
            _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
            return Task.CompletedTask;
        }
    }

    public override Task SendMessage(Users.User user, Character _, string message)
    {
        return SendMessage(user, message);
    }

    public override async Task SendMessage(Users.User user, string message)
    {
        if (user.TelegramUser == null)
        {
            _logger.LogWarning("User {UserId} does not have a Telegram user", user.Id);
            return;
        }
        if (_client == null)
        {
            _logger.LogWarning("Telegram client is not initialized");
            return;
        }

        int maxMessageLength = 4096;
        if (message.Length <= maxMessageLength)
        {
            await _client.SendMessage(user.TelegramUser.Id, message);
            return;
        }

        string[] chunks = message.Split(["\n\n"], StringSplitOptions.None);
        List<string> toSend = [];
        for (int i = 0; i < chunks.Length; i++)
        {
            string chunk = chunks[i];
            if (chunk.Length < maxMessageLength)
            {
                toSend.Add(chunk);
                continue;
            }

            for (int j = 0; j < chunk.Length; j += maxMessageLength)
            {
                string part = chunk.Substring(j, Math.Min(maxMessageLength, chunk.Length - j));
                toSend.Add(part);
            }
        }
        foreach (string chunk in toSend)
        {
            await _client.SendMessage(user.TelegramUser.Id, chunk);
            await Task.Delay(1000);
        }

        return;
    }

    public override Task SendMessage(Users.User user, Characters.Conversations.Message message)
    {
        if (message is TextMessage textMessage)
        {
            return SendMessage(user, textMessage.Text);
        }
        else
        {
            _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
            return Task.CompletedTask;
        }
    }

    private async Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        switch (update)
        {
            case { Message: { } message }:
                await OnMessage(message, cancellationToken);
                break;
            default:
                _logger.LogInformation("Received unhandled update of type {UpdateType}", update.Type);
                break;
        }
    }

    private async Task OnMessage(Telegram.Bot.Types.Message message, CancellationToken cancellationToken)
    {
        if (message is null || message.From?.Id is null || message.From?.Id == _me?.Id)
        {
            return;
        }

        FilterDefinition<Users.User> filter = Builders<Users.User>.Filter.Eq($"{nameof(Users.User.TelegramUser)}.{nameof(Users.User.TelegramUser.Id)}", message.From?.Id);
        Users.User? user = null;
        try
        {
            user = await _userDatabase.GetUser(filter);
        }
        catch (Exception)
        {
            // Do nothing, we will create a new user if needed
        }
        if (user == null)
        {
            if (message.From != null && message.Text != null && message.Text.StartsWith("/hello", StringComparison.InvariantCultureIgnoreCase))
            {
                user = new()
                {
                    Username = message.From.Username!,
                    Name = message.From.FirstName!,
                    LastUsedCommunicator = Name,
                    TelegramUser = new TelegramUser
                    {
                        Id = message.From.Id,
                        UserName = message!.From!.Username!
                    }
                };
                await _userDatabase.SaveDocumentAsync(user);
                _logger.LogInformation("User {UserId} created with Telegram ID {TelegramId}", user.Id, message.From.Id);
                return;
            }

            _logger.LogInformation("No valid user found for Telegram ID {TelegramId}", message?.From?.Id);

            return;
        }

        if (!user.Valid)
        {
            _logger.LogInformation("User {UserId} is not valid", user.Id);
            return;
        }

        await _client!.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);

        bool needsSave = false;
        if (user.Username != null && message?.From != null && message.From.Username != user.TelegramUser!.UserName)
        {
            user.TelegramUser.UserName = message!.From!.Username!;
            needsSave = true;
        }
        if (user.LastUsedCommunicator != Name)
        {
            user.LastUsedCommunicator = Name;
            needsSave = true;
        }
        if (needsSave == true)
        {
            await _userDatabase.SaveDocumentAsync(user);
        }

        if ((message?.Text?.StartsWith("/", StringComparison.InvariantCultureIgnoreCase) ?? false) ||
            (message?.Caption?.StartsWith("/", StringComparison.InvariantCultureIgnoreCase) ?? false))
        {
            _logger.LogInformation("Received command '{Command}' from user {UserId}", message.Text, user.Id);
            await HandleCommand(message, user);
            return;
        }

        if (message?.Text == null)
        {
            return;
        }

        _logger.LogInformation("Received text '{Text}' in chat {ChatId}", message.Text, message.Chat.Id);

        Character? character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacterId!);
        if (character == null)
        {
            if (string.IsNullOrEmpty(user.TelegramUser!.CurrentCharacterId) == false)
            {
                user.TelegramUser.CurrentCharacterId = string.Empty;
                await _userDatabase.SaveDocumentAsync(user);
            }

            await _client!.SendMessage(message.Chat.Id, "You do not have a current character. Please select one first!", cancellationToken: cancellationToken);
            return;
        }

        try
        {
            await RecieveMessage(user, character, message.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message '{Text}' from user {UserId}", message.Text, user.Id);
            await _client!.SendMessage(message.Chat.Id, "Failed to process message", cancellationToken: cancellationToken);
        }
    }
}
