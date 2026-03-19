using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.VoiceClips;
using Akagi.LLMs;
using Akagi.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Collections.ObjectModel;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    public override async Task SendMessage(Users.User user, Character character, Characters.Conversations.Message message)
    {
        await SendInfoIfCharacterSwitched(user, character);

        switch (message)
        {
            case TextMessage textMessage:
                await SendMessage(user, textMessage.Text);
                break;
            case VoiceMessage voiceMessage:
                {

                    VoiceClip? voiceClip = await _voiceClipsDatabase.GetDocumentByIdAsync(voiceMessage.VoiceId);
                    if (voiceClip == null)
                    {
                        _logger.LogWarning("Voice clip with ID {VoiceId} not found", voiceMessage.VoiceId);
                        return;
                    }
                    using Stream stream = await _voiceClipsDatabase.LoadFileAsync(voiceClip);
                    await SendAudio(user, stream, voiceClip.Id!);
                    break;
                }

            default:
                _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                break;
        }
    }

    public override async Task SendMessage(Users.User user, Character character, string message)
    {
        await SendInfoIfCharacterSwitched(user, character);
        await SendMessage(user, message);
    }

    private async Task SendInfoIfCharacterSwitched(Users.User user, Character character)
    {
        if (user.TelegramUser!.CurrentCharacterId == character.Id)
        {
            return;
        }

        user.TelegramUser.CurrentCharacterId = character.Id;
        await _userDatabase.SaveDocumentAsync(user);

        string infoMessage = $"You have switched to character: {character.Name}";
        await SendMessage(user, infoMessage);
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
        switch (message)
        {
            case TextMessage textMessage:
                return SendMessage(user, textMessage.Text);

            case CommandMessage commandMessage:
                return SendMessage(user, $"[Command] {commandMessage.Output}");

            default:
                _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
                return Task.CompletedTask;
        }
    }

    public override async Task SendAudio(Users.User user, Character character, Stream stream, string fileName)
    {
        await SendInfoIfCharacterSwitched(user, character);

        await SendAudio(user, stream, fileName);
    }

    public override async Task SendAudio(Users.User user, Stream stream, string fileName)
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

        await _client.SendAudio(user.TelegramUser.Id, stream, fileName);
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
                string llmDefinitionsId = await _lLMDefinitionDatabase.GetDefaultIdAsync() ?? throw new InvalidOperationException("No default llm found");

                user = new()
                {
                    Username = message.From.Username!,
                    Name = message.From.FirstName!,
                    LastUsedCommunicator = Name,
                    LLMPreferences = new ReadOnlyDictionary<string, string>(LLMDefinition.CreateDummyDictionary(llmDefinitionsId)),
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

        if (user.Username != null && message.From != null && message.From.Username != user.TelegramUser!.UserName)
        {
            user.TelegramUser.UserName = message.From.Username!;
        }
        if (user.LastUsedCommunicator != Name)
        {
            user.LastUsedCommunicator = Name;
        }
        if (user.Dirty == true)
        {
            await _userDatabase.SaveDocumentAsync(user);
        }

        if ((message.Text?.StartsWith("/", StringComparison.InvariantCultureIgnoreCase) ?? false) ||
            (message.Caption?.StartsWith("/", StringComparison.InvariantCultureIgnoreCase) ?? false))
        {
            _logger.LogInformation("Received command '{Command}' from user {UserId}", message.Text, user.Id);
            await HandleCommand(message, user);
            return;
        }

        Character? character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacterId!);
        if (character == null)
        {
            if (string.IsNullOrEmpty(user.TelegramUser!.CurrentCharacterId) == false)
            {
                user.TelegramUser.CurrentCharacterId = string.Empty;
                await _userDatabase.SaveDocumentAsync(user);
            }

            _logger.LogInformation("Received text '{Text}' in chat {ChatId} where user had no character selected.", message.Text, message.Chat.Id);
            await _client!.SendMessage(message.Chat.Id, "You do not have a current character. Please select one first!", cancellationToken: cancellationToken);
            return;
        }

        Characters.Conversations.Message toProcess;
        switch (message.Type)
        {
            case MessageType.Text:

                if (message.Text == null)
                {
                    _logger.LogWarning("Received text message with null text in chat {ChatId}", message.Chat.Id);
                    await _client!.SendMessage(message.Chat.Id, "Received text message with null text, cannot process.", cancellationToken: cancellationToken);
                    return;
                }
                toProcess = new TextMessage
                {
                    From = Characters.Conversations.Message.Type.User,
                    Text = message.Text,
                    Time = DateTime.UtcNow,
                };
                break;

            case MessageType.Voice:
                {
                    if (message.Voice == null)
                    {
                        _logger.LogWarning("Received voice message with null voice in chat {ChatId}", message.Chat.Id);
                        await _client!.SendMessage(message.Chat.Id, "Received voice message with null voice, cannot process.", cancellationToken: cancellationToken);
                        return;
                    }

                    if (message.Voice.FileSize > 20 * 1024 * 1024)
                    {
                        _logger.LogInformation("Received voice message that is too large ({FileSize} bytes) in chat {ChatId}", message.Voice.FileSize, message.Chat.Id);
                        await _client!.SendMessage(message.Chat.Id, "Voice message is too large. Maximum size is 20mb.", cancellationToken: cancellationToken);
                        return;
                    }

                    await using MemoryStream? stream = await LoadFile(message.Voice);
                    if (stream == null)
                    {
                        _logger.LogWarning("Failed to load voice message file in chat {ChatId}", message.Chat.Id);
                        await _client!.SendMessage(message.Chat.Id, "Failed to load voice message file, cannot process.", cancellationToken: cancellationToken);
                        return;
                    }
                    VoiceClip voiceClip = new()
                    {
                        AudioEncoding = AudioEncoding.OGG_OPUS,
                        Text = null, // TODO: Consider transcribing the voice message to text
                    };
                    await _voiceClipsDatabase.SaveFileAsync(voiceClip, stream);

                    toProcess = new VoiceMessage
                    {
                        From = Characters.Conversations.Message.Type.User,
                        VoiceId = voiceClip.Id!,
                        Time = DateTime.UtcNow,
                    };
                    break;
                }

            default:
                _logger.LogInformation("Received message of type {MessageType} which is not handled", message.Type);
                await _client!.SendMessage(message.Chat.Id, $"Message type {message.Type} is not supported yet.", cancellationToken: cancellationToken);
                return;
        }

        _logger.LogInformation("Received message '{Message}' in chat {ChatId}", toProcess.ToString(), message.Chat.Id);
        try
        {
            await ReceiveMessage(user, character, toProcess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message '{Message}' from user {UserId}", toProcess.ToString(), user.Id);
            await _client!.SendMessage(message.Chat.Id, "Failed to process message", cancellationToken: cancellationToken);
        }
        finally
        {
            _logger.LogInformation("Finished chat {ChatId} message", message.Chat.Id);
        }
    }
}
