using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication.Commands;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Data;
using Akagi.Receivers;
using Akagi.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;

namespace Akagi.Communication.SocketComs;

internal class SocketService : Communicator, IHostedService
{
    internal class Options
    {
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
    }

    public override string Name => "Socket";

    public override Command[] AvailableCommands => _commands;

    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SocketService> _logger;
    private readonly Command[] _commands;
    private readonly SocketTransmissionHandler[] _transmissionHandlers;
    private readonly ConcurrentBag<SocketSession> _sessions = [];
    private readonly Options _options;

    private SocketServer? _server;

    public SocketService(IReceiver receiver,
                         IDatabaseFactory databaseFactory,
                         IOptionsMonitor<Options> options,
                         ILoggerFactory loggerFactory,
                         IEnumerable<Command> commands,
                         IEnumerable<SocketTransmissionHandler> transmissionHandlers) : base(receiver)
    {
        _options = options.CurrentValue;

        _databaseFactory = databaseFactory;

        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SocketService>();

        _commands = [..commands.Where(x => x.CompatibleFor
                               .All(y => typeof(SocketService).IsAssignableTo(y)))];
        Array.ForEach(_commands, x => x.Init(this));

        _transmissionHandlers = [.. transmissionHandlers];

        _logger.LogInformation("SocketService initialized with commands: {Commands}",
            string.Join(", ", _commands.OrderBy(x => x.Name).Select(x => x.Name)));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_server != null)
        {
            _logger.LogWarning("SocketService is already running on {Ip}:{Port}", _options.Ip, _options.Port);
            return Task.CompletedTask;
        }

        _server = new SocketServer(IPAddress.Parse(_options.Ip), _options.Port, this, _loggerFactory);
        _server.Start();

        _logger.LogInformation("SocketService started on {Ip}:{Port}", _options.Ip, _options.Port);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server != null)
        {
            _server.DisconnectAll();
            _server.Stop();
            _server.Dispose();
            _server = null;
            _sessions.Clear();

            _logger.LogInformation("SocketService stopped on {Ip}:{Port}", _options.Ip, _options.Port);
        }
        return Task.CompletedTask;
    }

    public void AddSession(SocketSession session)
    {
        if (_server == null)
        {
            _logger.LogError("Cannot add session. Socket server is not running.");
            return;
        }

        _sessions.Add(session);
        _logger.LogInformation("Session {SessionId} added. Total sessions: {Count}", session.Id, _sessions.Count);
    }

    public void RemoveSession(SocketSession session)
    {
        if (_server == null)
        {
            _logger.LogError("Cannot remove session. Socket server is not running.");
            return;
        }

        if (_sessions.TryTake(out _))
        {
            _logger.LogInformation("Session {SessionId} removed. Total sessions: {Count}", session.Id, _sessions.Count);
        }
        else
        {
            _logger.LogWarning("Failed to remove session {SessionId}. Session not found.", session.Id);
        }
    }

    public override Task SendMessage(User user, Character character, string message)
    {
        TextMessageResponseTransmission sendText = new()
        {
            Message = new Bridge.Chat.Models.TextMessage
            {
                From = Message.ToBridgeType(Message.Type.System),
                Text = message,
                Time = DateTime.UtcNow
            },
            CharacterId = character.Id,
        };

        SendTransmission(user, sendText);
        return Task.CompletedTask;
    }

    public override Task SendMessage(User user, Character character, Message message)
    {
        if (message is TextMessage textMessage)
        {
            TextMessageResponseTransmission sendText = new()
            {
                Message = new Bridge.Chat.Models.TextMessage
                {
                    From = Message.ToBridgeType(message.From),
                    Text = textMessage.Text,
                    Time = textMessage.Time,
                },
                CharacterId = character.Id
            };

            SendTransmission(user, sendText);
            return Task.CompletedTask;
        }
        else
        {
            _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
            return Task.CompletedTask;
        }
    }

    public override Task SendMessage(User user, string message)
    {
        TextMessageResponseTransmission sendText = new()
        {
            Message = new Bridge.Chat.Models.TextMessage
            {
                From = Message.ToBridgeType(Message.Type.System),
                Text = message,
                Time = DateTime.UtcNow
            },
            CharacterId = null,
        };

        SendTransmission(user, sendText);
        return Task.CompletedTask;
    }

    public override Task SendMessage(User user, Message message)
    {
        if (message is TextMessage textMessage)
        {
            TextMessageResponseTransmission sendText = new()
            {
                Message = new Bridge.Chat.Models.TextMessage
                {
                    From = Message.ToBridgeType(message.From),
                    Text = textMessage.Text,
                    Time = textMessage.Time,
                },
                CharacterId = null
            };

            SendTransmission(user, sendText);
            return Task.CompletedTask;
        }
        else
        {
            _logger.LogWarning("Unknown message type: {MessageType}", message.GetType());
            return Task.CompletedTask;
        }
    }

    public async Task<bool> RecieveTransmission(SocketSession socketSession, TransmissionWrapper wrapper)
    {
        SocketTransmissionHandler? transmissionHandler = _transmissionHandlers
            .FirstOrDefault(x => x.HandlesType == wrapper.MessageType);

        if (transmissionHandler == null)
        {
            _logger.LogWarning("No transmission handler found for type: {MessageType}", wrapper.MessageType);
            return false;
        }

        _logger.LogInformation("Recieved transmission: {TransmissionType}", wrapper.MessageType);

        await using SocketTransmissionHandler.Context context = new()
        {
            DatabaseFactory = _databaseFactory,
            Service = this,
            Session = socketSession,
        };

        try
        {
            await transmissionHandler.ExecuteAsync(context, wrapper);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing transmission handler for type: {MessageType}", wrapper.MessageType);
            return false;
        }

        return true;
    }

    public void SendTransmission<T>(User user, T transmission) where T : Transmission
    {
        SocketSession[] sessions = GetSessions(user);
        if (sessions.Length == 0)
        {
            _logger.LogInformation("No active sessions found for user {UserId}. Cannot send transmission.", user.Id);
            return;
        }
        foreach (SocketSession session in sessions)
        {
            session.SendTransmission(transmission);
        }
        _logger.LogInformation("Sent transmission to {Count} sessions for user {UserId}.", sessions.Length, user.Id);
    }

    public Task RecieveText(User user, Character character, string text)
    {
        return RecieveMessage(user, character, text);
    }

    private SocketSession[] GetSessions(User user)
    {
        return [.. _sessions.Where(session => session.User?.Id == user.Id)];
    }
}
