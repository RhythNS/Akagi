using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.Conversations;
using Akagi.Communication.Commands;
using Akagi.Puppeteers;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Collections.Generic;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Akagi.Communication.TelegramComs;

internal class TelegramService : Communicator, IHostedService
{
    internal class Options
    {
        public string Token { get; set; } = string.Empty;
    }

    private readonly string _token;
    private readonly ILogger<TelegramService> _logger;
    private readonly IUserDatabase _userDatabase;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly ICardDatabase _cardDatabase;
    private readonly ISystemProcessorDatabase _systemProcessorDatabase;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly TextCommand[] _textCommands = [];
    private readonly DocumentCommand[] _documentCommands = [];

    private TelegramBotClient? _client;
    private Telegram.Bot.Types.User? _me;
    private const int MaxRestartAttempts = 5;

    public TelegramService(IPuppeteer puppeteer,
                           ISystemProcessorDatabase systemProcessorDatabase,
                           IOptionsMonitor<Options> options,
                           IUserDatabase userDatabase,
                           ICharacterDatabase characterDatabase,
                           ICardDatabase cardDatabase,
                           IEnumerable<Command> _commands,
                           ILogger<TelegramService> logger,
                           IHostApplicationLifetime hostApplicationLifetime) : base(puppeteer, systemProcessorDatabase)
    {
        _token = options.CurrentValue.Token;
        _logger = logger;
        _characterDatabase = characterDatabase;
        _userDatabase = userDatabase;
        _cardDatabase = cardDatabase;
        _systemProcessorDatabase = systemProcessorDatabase;
        _hostApplicationLifetime = hostApplicationLifetime;

        _textCommands = _commands.OfType<TextCommand>().ToArray();
        _documentCommands = _commands.OfType<DocumentCommand>().ToArray();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Telegram bot client!");

        _client = new TelegramBotClient(_token);
        _me = await _client.GetMe(cancellationToken);

        _client.StartReceiving(
            HandleUpdate,
            HandleErrorAsync,
            new ReceiverOptions(),
            cancellationToken
        );

        _logger.LogInformation("Bot client started with username {Username}", _me.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Telegram bot client!");
        _client?.Close(cancellationToken);
        return Task.CompletedTask;
    }

    private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An error occurred in the bot client.");

        for (int restartAttempts = 0; restartAttempts < MaxRestartAttempts; restartAttempts++)
        {
            int delay = (int)Math.Pow(2, restartAttempts);
            _logger.LogInformation("Attempting to restart Telegram bot in {Delay} seconds (attempt {Attempt}/{Max})", delay, restartAttempts, MaxRestartAttempts);
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);

            try
            {
                await StopAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await StartAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart Telegram bot.");
            }
        }

        _logger.LogCritical("Max restart attempts reached. Stopping application.");
        _hostApplicationLifetime.StopApplication();
    }

    public async Task<MemoryStream?> LoadFile(FileBase fileBase)
    {
        if (_client == null)
        {
            _logger.LogWarning("Telegram client is not initialized");
            return null;
        }

        TGFile file = await _client.GetFile(fileBase.FileId);
        if (file == null || file.FilePath == null)
        {
            _logger.LogWarning("Failed to get file for FileId {FileId}", fileBase.FileId);
            return null;
        }

        using MemoryStream stream = new();
        await _client.DownloadFile(file.FilePath, stream);
        return stream;
    }

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

        await _client.SendMessage(user.TelegramUser.Id, message);
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
                    TelegramUser = new TelegramUser
                    {
                        Id = message.From.Id
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
        if (user.Username != null && message?.From != null && message.From.Username != user.TelegramUser!.UserName)
        {
            user.TelegramUser.UserName = message!.From!.Username!;
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

        Character character = await _characterDatabase.GetCharacter(user.TelegramUser!.CurrentCharacter!);

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

        TextCommand? textCommand = _textCommands.FirstOrDefault(c => c.Name.Equals(command, StringComparison.InvariantCultureIgnoreCase));
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

        DocumentCommand? documentCommand = _documentCommands.FirstOrDefault(c => c.Name.Equals(command, StringComparison.InvariantCultureIgnoreCase));
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
