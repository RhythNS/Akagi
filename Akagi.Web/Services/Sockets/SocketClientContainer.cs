using Akagi.Web.Models;
using Akagi.Web.Services.Circuits;
using Akagi.Web.Services.Users;

namespace Akagi.Web.Services.Sockets;

public interface ISocketClientContainer
{
    public bool IsConnected { get; }
    public Task<SocketClient?> EnsureConnectedAsync();
    public Task TryConnectAsync();
    public Task TryDisconnectAsync();
    public SocketClient? Client { get; }
    public event Action? ConnectionStateChanged;
}

public class SocketClientContainer : ISocketClientContainer, IDisposable
{
    public SocketClient? Client { get; private set; }
    public event Action? ConnectionStateChanged;

    private readonly ISocketService _socketService;
    private readonly ICircuitIdAccessor _circuitIdAccessor;
    private readonly IUserState _userState;
    private readonly ILogger<SocketClientContainer> _logger;

    private Task? _connectTask;
    private CancellationTokenSource? _reconnectCts;
    private bool _manualDisconnect;
    private bool _pendingConnectOnCircuitOpen;
    private bool _disposed;

    private static readonly int[] BackoffDelaysMs = [1000, 2000, 4000, 8000, 15000];

    public SocketClientContainer(ISocketService socketService,
                                 ICircuitIdAccessor circuitIdAccessor,
                                 IUserState userState,
                                 ILogger<SocketClientContainer> logger)
    {
        _socketService = socketService;
        _circuitIdAccessor = circuitIdAccessor;
        _userState = userState;
        _logger = logger;

        _circuitIdAccessor.CircuitOpened += OnCircuitOpened;
    }

    public bool IsConnected => Client?.IsConnected ?? false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _circuitIdAccessor.CircuitOpened -= OnCircuitOpened;
        CancelAutoReconnect();

        if (Client != null)
        {
            Client.ConnectionStateChanged -= OnClientConnectionStateChanged;
            Client.Dispose();
            Client = null;
        }

        ConnectionStateChanged = null;
        GC.SuppressFinalize(this);
    }

    private void OnCircuitOpened()
    {
        if (_disposed || !_pendingConnectOnCircuitOpen || _manualDisconnect)
        {
            return;
        }

        _pendingConnectOnCircuitOpen = false;
        _ = TryConnectAsync();
    }

    public async Task<SocketClient?> EnsureConnectedAsync()
    {
        if (_disposed)
        {
            return null;
        }

        if (Client is { IsConnected: true })
        {
            return Client;
        }

        // Skip opening a throwaway TCP connection during HTTP static prerendering (when no SignalR circuit exists yet)
        if (string.IsNullOrEmpty(_circuitIdAccessor.CircuitId))
        {
            _pendingConnectOnCircuitOpen = true;
            return null;
        }

        await TryConnectAsync();
        return IsConnected ? Client : null;
    }

    public async Task TryConnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrEmpty(_circuitIdAccessor.CircuitId))
        {
            _pendingConnectOnCircuitOpen = true;
            return;
        }

        _manualDisconnect = false;
        CancelAutoReconnect();

        Task? existingTask = _connectTask;
        if (existingTask != null && !existingTask.IsCompleted)
        {
            _logger.LogInformation("Connection attempt already in progress; awaiting completion.");
            await existingTask;
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

            if (user == null || !user.Valid)
            {
                _logger.LogError("User was null or invalid when connecting socket client.");
                return;
            }

            if (Client != null)
            {
                if (Client.IsConnected)
                {
                    _logger.LogInformation("Already connected.");
                    return;
                }

                Client.ConnectionStateChanged -= OnClientConnectionStateChanged;
                Client.Dispose();
                Client = null;
            }

            SocketClient newClient = await _socketService.GetNew(user, _circuitIdAccessor.CircuitId);
            newClient.ConnectionStateChanged += OnClientConnectionStateChanged;
            Client = newClient;

            NotifyConnectionStateChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to the socket service.");
            NotifyConnectionStateChanged();
        }
    }

    public Task TryDisconnectAsync()
    {
        _manualDisconnect = true;
        _pendingConnectOnCircuitOpen = false;
        CancelAutoReconnect();

        if (Client == null)
        {
            _logger.LogInformation("No socket client to disconnect.");
            return Task.CompletedTask;
        }

        try
        {
            Client.ConnectionStateChanged -= OnClientConnectionStateChanged;
            Client.Dispose();
            Client = null;
            _logger.LogInformation("Disconnected from the socket service.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disconnect from the socket service.");
        }
        finally
        {
            NotifyConnectionStateChanged();
        }

        return Task.CompletedTask;
    }

    private void OnClientConnectionStateChanged()
    {
        NotifyConnectionStateChanged();

        if (!_disposed && !_manualDisconnect && !IsConnected)
        {
            StartAutoReconnect();
        }
    }

    private void StartAutoReconnect()
    {
        if (_reconnectCts != null)
        {
            return;
        }

        CancellationTokenSource cts = new();
        _reconnectCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                for (int attempt = 0; attempt < BackoffDelaysMs.Length; attempt++)
                {
                    if (cts.Token.IsCancellationRequested || _disposed || _manualDisconnect || IsConnected)
                    {
                        return;
                    }

                    int delayMs = BackoffDelaysMs[attempt];
                    _logger.LogInformation("Attempting socket auto-reconnect (attempt {Attempt}/{Max}) in {Delay}ms...",
                        attempt + 1, BackoffDelaysMs.Length, delayMs);

                    await Task.Delay(delayMs, cts.Token);

                    if (cts.Token.IsCancellationRequested || _disposed || _manualDisconnect || IsConnected)
                    {
                        return;
                    }

                    Task connectTask = _connectTask = Connect();
                    await connectTask;

                    if (IsConnected)
                    {
                        _logger.LogInformation("Socket auto-reconnect succeeded on attempt {Attempt}.", attempt + 1);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Reconnect cancelled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during socket auto-reconnect.");
            }
            finally
            {
                if (ReferenceEquals(_reconnectCts, cts))
                {
                    _reconnectCts = null;
                }
                cts.Dispose();
            }
        }, cts.Token);
    }

    private void CancelAutoReconnect()
    {
        CancellationTokenSource? cts = _reconnectCts;
        _reconnectCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch
            {
                // Ignore disposal races
            }
        }
    }

    private void NotifyConnectionStateChanged()
    {
        try
        {
            ConnectionStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying ConnectionStateChanged subscribers.");
        }
    }
}
