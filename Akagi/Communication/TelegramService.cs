using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Puppeteers;
using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Akagi.Communication;

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


    private TelegramBotClient? _client;
    private Telegram.Bot.Types.User? _me;
    private const int MaxRestartAttempts = 5;

    public TelegramService(IPuppeteer puppeteer,
                           ISystemProcessorDatabase systemProcessorDatabase,
                           IOptionsMonitor<Options> options,
                           IUserDatabase userDatabase,
                           ICharacterDatabase characterDatabase,
                           ICardDatabase cardDatabase,
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

    public override Task SendMessage(Users.User user, Character character, Characters.Message message)
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

    public override async Task SendMessage(Users.User user, Character _, string message)
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

        if (command.StartsWith("/username", StringComparison.InvariantCultureIgnoreCase))
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _client.SendMessage(message.Chat.Id, "Please provide a username");
                return;
            }
            string username = parts[1];
            user.Username = username;
            await _userDatabase.SaveDocumentAsync(user);
            await _client.SendMessage(message.Chat.Id, $"Username set to {username}");
        }
        else if (command.StartsWith("/name", StringComparison.InvariantCultureIgnoreCase))
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await _client.SendMessage(message.Chat.Id, "Please provide a name");
                return;
            }
            string name = parts[1];
            user.Name = name;
            await _userDatabase.SaveDocumentAsync(user);
            await _client.SendMessage(message.Chat.Id, $"Name set to {name}");
        }
        else if (command.StartsWith("/ping"))
        {
            await _client.SendMessage(message.Chat.Id, "Pong!");
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
        else if (command.StartsWith("/listCharacters"))
        {
            List<Character> characters = await _characterDatabase.GetCharactersForUser(user);
            if (characters.Count == 0)
            {
                await _client.SendMessage(message.Chat.Id, "No characters found");
                return;
            }
            string[] ids = characters.Select(x => x.Id).ToArray();
            string[] names = characters.Select(x => x.Card.Name).ToArray();
            string choices = GetList(ids, names);
            await _client.SendMessage(message.Chat.Id, $"Available characters:\n{choices}");
        }
        else if (command.StartsWith("/listCards"))
        {
            List<Card> cards = await _cardDatabase.GetDocumentsAsync();
            if (cards.Count == 0)
            {
                await _client.SendMessage(message.Chat.Id, "No cards found");
                return;
            }
            string[] ids = cards.Select(x => x.Id).ToArray();
            string[] names = cards.Select(x => x.Name).ToArray();
            string choices = GetCommandListChoice("createCharacter", ids, names);
            await _client.SendMessage(message.Chat.Id, $"Available cards:\n{choices}");
        }
        else if (command.StartsWith("/listSystemProcessors"))
        {
            List<SystemProcessor> systemProcessors = await _systemProcessorDatabase.GetDocumentsAsync();
            if (systemProcessors.Count == 0)
            {
                await _client.SendMessage(message.Chat.Id, "No system processors found");
                return;
            }
            string[] ids = systemProcessors.Select(x => x.Id).ToArray();
            string[] names = systemProcessors.Select(x => x.Name).ToArray();
            string choices = GetList(ids, names);
            await _client.SendMessage(message.Chat.Id, $"Available system processors:\n{choices}");
        }
        else if (command.StartsWith("/createCharacter"))
        {
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                await _client.SendMessage(message.Chat.Id, "Please provide a card id and processor id");
                return;
            }

            string id = parts[1];
            Card? card = await _cardDatabase.GetDocumentByIdAsync(id);
            if (card == null)
            {
                await _client.SendMessage(message.Chat.Id, "Card not found");
                return;
            }

            string processorId = parts[2];
            SystemProcessor? systemProcessor = await _systemProcessorDatabase.GetDocumentByIdAsync(processorId);
            if (systemProcessor == null)
            {
                await _client.SendMessage(message.Chat.Id, "System processor not found");
                return;
            }

            Character character = new()
            {
                SystemProcessorId = systemProcessor.Id,
                CardId = card.Id,
                UserId = user.Id,
            };
            await _characterDatabase.SaveDocumentAsync(character);

            await _client.SendMessage(message.Chat.Id, $"Character created with card {card.Name} and system processor {systemProcessor.Name}");
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
        else if (command.StartsWith("/test"))
        {
            await _client.SendMessage(message.Chat.Id, "/test\n\\test");
        }
        else
        {
            await _client.SendMessage(message.Chat.Id, "Unknown command");
        }
    }

    private static string GetList(string[] ids, string[] names)
    {
        if (ids.Length != names.Length)
        {
            throw new ArgumentException("Commands and names must have the same length");
        }
        StringBuilder sb = new();
        for (int i = 0; i < ids.Length; i++)
        {
            sb.Append(names[i]);
            sb.Append(" (");
            sb.Append(ids[i]);
            sb.Append(")");
            if (i < ids.Length - 1)
            {
                sb.Append(", ");
            }
        }
        return sb.ToString();
    }

    private static string GetCommandListChoice(string command, string[] ids, string[] names)
    {
        if (ids.Length != names.Length)
        {
            throw new ArgumentException("Commands and names must have the same length");
        }

        StringBuilder sb = new();
        for (int i = 0; i < ids.Length; i++)
        {
            sb.Append(names[i]);
            sb.Append(":\n/");
            sb.Append(command);
            sb.Append(" ");
            sb.Append(ids[i]);
            if (i < ids.Length - 1)
            {
                sb.Append("\n");
            }
        }

        return sb.ToString();
    }

    private async Task HandleDocumentCommand(Telegram.Bot.Types.Message message, Users.User user)
    {
        if (_client == null || message.Caption == null)
        {
            return;
        }

        string command = message.Caption;

        if (command.StartsWith("/uploadCard", StringComparison.InvariantCultureIgnoreCase))
        {
            List<FileBase> validFiles = [];

            if (message.Document != null)
            {
                validFiles.Add(message.Document);
            }
            else if (message.Photo != null && message.Photo.Length > 0)
            {
                validFiles.AddRange(message.Photo);
            }

            if (validFiles.Count == 0)
            {
                await _client.SendMessage(message.Chat.Id, "Please upload valid files or images.");
                return;
            }

            int successCount = 0;

            foreach (var fileBase in validFiles)
            {
                TGFile file = await _client.GetFile(fileBase.FileId);
                if (file == null || file.FilePath == null)
                {
                    _logger.LogWarning("Failed to get file for FileId {FileId}", fileBase.FileId);
                    continue;
                }

                using MemoryStream stream = new();
                await _client.DownloadFile(file.FilePath, stream);
                bool success = await _cardDatabase.SaveCardFromImage(stream);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    _logger.LogWarning("Failed to save card from image for FileId {FileId}", fileBase.FileId);
                }
            }

            await _client.SendMessage(message.Chat.Id, $"{successCount} file(s) processed successfully.");
        }
        if (command.StartsWith("/uploadSystemProcessor", StringComparison.InvariantCultureIgnoreCase))
        {
            if (message.Document == null)
            {
                await _client.SendMessage(message.Chat.Id, "Please upload a valid file.");
                return;
            }

            TGFile file = await _client.GetFile(message.Document.FileId);
            if (file == null || file.FilePath == null)
            {
                _logger.LogWarning("Failed to get file for FileId {FileId}", message.Document.FileId);
                return;
            }
            using MemoryStream stream = new();
            await _client.DownloadFile(file.FilePath, stream);
            bool success = await _systemProcessorDatabase.SaveSystemProcessorFromFile(stream);
            if (success)
            {
                await _client.SendMessage(message.Chat.Id, "System processor uploaded successfully.");
            }
            else
            {
                _logger.LogWarning("Failed to save system processor from file for FileId {FileId}", message.Document.FileId);
                await _client.SendMessage(message.Chat.Id, "Failed to save system processor from file.");
            }
        }
        else
        {
            await _client.SendMessage(message.Chat.Id, "Unknown command");
        }
    }
}
