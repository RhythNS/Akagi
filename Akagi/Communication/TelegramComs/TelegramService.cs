using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Communication.Commands;
using Akagi.Receivers;
using Akagi.Receivers.SystemProcessors;
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

    private readonly string _token;
    private readonly ILogger<TelegramService> _logger;
    private readonly IUserDatabase _userDatabase;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly TextCommand[] _textCommands = [];
    private readonly DocumentCommand[] _documentCommands = [];

    private TelegramBotClient? _client;
    private Telegram.Bot.Types.User? _me;
    private const int MaxRestartAttempts = 5;

    public TelegramService(IReceiver receiver,
                           IOptionsMonitor<Options> options,
                           IUserDatabase userDatabase,
                           ICharacterDatabase characterDatabase,
                           IEnumerable<Command> _commands,
                           ILogger<TelegramService> logger,
                           IHostApplicationLifetime hostApplicationLifetime) : base(receiver)
    {
        _token = options.CurrentValue.Token;
        _logger = logger;
        _characterDatabase = characterDatabase;
        _userDatabase = userDatabase;
        _hostApplicationLifetime = hostApplicationLifetime;

        _textCommands = _commands.OfType<TextCommand>().ToArray();
        Array.ForEach(_textCommands, x => x.Init(this));

        _documentCommands = _commands.OfType<DocumentCommand>().ToArray();
        Array.ForEach(_documentCommands, x => x.Init(this));
    }
}
