using Akagi.Characters;
using Akagi.Communication.Commands;
using Akagi.Data;
using Akagi.Receivers;
using Akagi.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Akagi.Communication.TelegramComs;

internal partial class TelegramService : Communicator, IHostedService
{
    internal class Options
    {
        public string Token { get; set; } = string.Empty;
    }

    public override string Name => "Telegram";

    public override Command[] AvailableCommands => [.. _textCommands.Cast<Command>(), .. _documentCommands.Cast<Command>()];

    private readonly string _token;
    private readonly ILogger<TelegramService> _logger;
    private readonly IUserDatabase _userDatabase;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IDatabaseFactory _databaseFactory;
    private readonly TextCommand[] _textCommands = [];
    private readonly DocumentCommand[] _documentCommands = [];

    private TelegramBotClient? _client;
    private Telegram.Bot.Types.User? _me;
    private const int MaxRestartAttempts = 5;

    public TelegramService(IReceiver receiver,
                           IOptionsMonitor<Options> options,
                           IUserDatabase userDatabase,
                           ICharacterDatabase characterDatabase,
                           IDatabaseFactory databaseFactory,
                           IEnumerable<Command> commands,
                           ILogger<TelegramService> logger,
                           IHostApplicationLifetime hostApplicationLifetime) : base(receiver)
    {
        _token = options.CurrentValue.Token;
        _logger = logger;
        _characterDatabase = characterDatabase;
        _databaseFactory = databaseFactory;
        _userDatabase = userDatabase;
        _hostApplicationLifetime = hostApplicationLifetime;

        Command[] validCommands = [..commands.Where(x => x.CompatibleFor
                                             .All(y => typeof(TelegramService).IsAssignableTo(y)))];

        _textCommands = [.. validCommands.OfType<TextCommand>()];
        Array.ForEach(_textCommands, x => x.Init(this));

        _documentCommands = [.. validCommands.OfType<DocumentCommand>()];
        Array.ForEach(_documentCommands, x => x.Init(this));

        string validCommandsList = string.Join(", ", validCommands.OrderBy(x => x.Name).Select(x => x.Name));
        _logger.LogInformation("TelegramService initialized with commands: {Commands}", validCommandsList);
    }
}
