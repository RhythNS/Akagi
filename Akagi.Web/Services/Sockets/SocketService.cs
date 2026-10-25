using Akagi.Web.Models;
using Akagi.Web.Services.Sockets.Requests;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;

namespace Akagi.Web.Services.Sockets;

public interface ISocketService
{
    public Task<SocketClient> GetNew(User user, string circuitId);
    public SocketClient? Find(User user, string circuitId);

    public void OnDeleted(SocketClient socketClient);
}

public class SocketService : ISocketService
{
    public class Options
    {
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
    }

    private readonly Options _options;
    private readonly SocketTransmissionHandler[] _socketTransmissionHandlers;
    private readonly ConcurrentDictionary<(string UserId, string CircuitId), SocketClient> _clients = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SocketService> _logger;

    public SocketService(IOptionsMonitor<Options> options,
                         ILoggerFactory loggerFactory,
                         IEnumerable<SocketTransmissionHandler> socketTransmissionHandlers)
    {
        _options = options.CurrentValue;
        _socketTransmissionHandlers = [.. socketTransmissionHandlers];
        _loggerFactory = loggerFactory;

        _logger = loggerFactory.CreateLogger<SocketService>();
    }

    public async Task<SocketClient> GetNew(User user, string circuitId)
    {
        string? token = user.GoogleToken;

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogError("Failed to retrieve Google ID token for user {UserId}.", user.Id);
            throw new InvalidOperationException("Google ID token is required to create a socket client.");
        }

        SocketClient socketClient = new(IPAddress.Parse(_options.Ip),
                                        _options.Port,
                                        user,
                                        circuitId,
                                        _loggerFactory,
                                        _socketTransmissionHandlers,
                                        this);

        try
        {
            bool connected = await socketClient.ConnectAndWaitAsync(TimeSpan.FromSeconds(5));
            if (!connected)
            {
                throw new TimeoutException("Timed out waiting for socket client TCP connection.");
            }

            await new LoginRequest(token).Get(socketClient);
        }
        catch (Exception)
        {
            _logger.LogError("Failed to login socket client for user {UserId} and circuit {CircuitId}.", user.Id, circuitId);
            socketClient.Dispose();
            throw;
        }

        _clients[(user.Id!, circuitId)] = socketClient;

        return socketClient;
    }

    public SocketClient? Find(User user, string circuitId)
    {
        if (!string.IsNullOrEmpty(circuitId) &&
            _clients.TryGetValue((user.Id!, circuitId), out SocketClient? client) &&
            client.IsConnected)
        {
            return client;
        }

        SocketClient? fallback = _clients.Values.FirstOrDefault(c => c.User.Id == user.Id && c.IsConnected);
        if (fallback != null)
        {
            return fallback;
        }

        _logger.LogWarning("Socket client not found for user {UserId} and circuit {CircuitId}.", user.Id, circuitId);
        return null;
    }

    public void OnDeleted(SocketClient socketClient)
    {
        ((ICollection<KeyValuePair<(string UserId, string CircuitId), SocketClient>>)_clients)
            .Remove(new KeyValuePair<(string UserId, string CircuitId), SocketClient>(
                (socketClient.User.Id!, socketClient.CircuitId),
                socketClient));
    }
}
