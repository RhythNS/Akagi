using Akagi.Web.Models;
using Akagi.Web.Services.Circuits;
using Akagi.Web.Services.Users;

namespace Akagi.Web.Services.Sockets;

public interface ISocketClientContainer
{
    public bool IsConnected { get; }
    public Task TryConnectAsync();
    public Task TryDisconnectAsync();
    public SocketClient? Client { get; }
}

public class SocketClientContainer : ISocketClientContainer, IDisposable
{
    public SocketClient? Client { get; private set; }

    private readonly ISocketService _socketService;
    private readonly ICircuitIdAccessor _circuitIdAccessor;
    private readonly IUserState _userState;
    private readonly ILogger<SocketClientContainer> _logger;

    private Task? _connectTask;

    public SocketClientContainer(ISocketService socketService,
                                 ICircuitIdAccessor circuitIdAccessor,
                                 IUserState userState,
                                 ILogger<SocketClientContainer> logger)
    {
        _socketService = socketService;
        _circuitIdAccessor = circuitIdAccessor;
        _userState = userState;
        _logger = logger;
    }

    public bool IsConnected => Client?.IsConnected ?? false;

    public void Dispose()
    {
        Client?.Dispose();
        Client = null;
        GC.SuppressFinalize(this);
    }

    public async Task TryConnectAsync()
    {
        if (_connectTask != null && !_connectTask.IsCompleted)
        {
            _logger.LogInformation("Connection attempt already in progress.");
            return;
        }
        _connectTask = Connect();
        await _connectTask;
    }

    private async Task Connect()
    {
        try
        {
            User user = await _userState.GetCurrentUserAsync();

            if (user == null)
            {
                _logger.LogError("User was null");
                return;
            }

            if (Client != null)
            {
                if (Client.IsConnected)
                {
                    _logger.LogInformation("Already connected.");
                    return;
                }

                Client.Dispose();
                Client = null;
            }

            Client = await _socketService.GetNew(user, _circuitIdAccessor.CircuitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to the socket service.");
        }
    }

    public Task TryDisconnectAsync()
    {
        if (Client == null)
        {
            _logger.LogInformation("No socket client to disconnect.");
            return Task.CompletedTask;
        }
        try
        {
            Client.Dispose();
            Client = null;
            _logger.LogInformation("Disconnected from the socket service.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect from the socket service.");
        }
        return Task.CompletedTask;
    }
}
