using Akagi.Bridge.Chat.Transmissions;
using Akagi.Web.Models;
using Akagi.Web.Models.Chat;
using Akagi.Web.Services.Sockets.Requests;
using MessagePack;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Akagi.Web.Services.Sockets;

public class SocketClient : NetCoreServer.TcpClient
{
    public delegate void OnMessageRecieved(string? characterId, Message message);

    public User User { get; init; }
    public string CircuitId { get; init; }
    public event OnMessageRecieved? MessageRecieved;

    private readonly SocketTransmissionHandler[] _transmissionHandlers;
    private readonly SocketService _socketService;
    private readonly TransmissionPackageBuilder _packagedBuilder;
    private readonly ConcurrentDictionary<IRequest, byte> _requests = new();
    private readonly ILogger<SocketClient> _logger;

    public SocketClient(IPAddress address,
                        int port,
                        User user,
                        string circuitId,
                        ILoggerFactory loggerFactory,
                        IEnumerable<SocketTransmissionHandler> transmissionHandlers,
                        SocketService socketService) : base(address, port)
    {
        User = user;
        CircuitId = circuitId;
        _logger = loggerFactory.CreateLogger<SocketClient>();
        _transmissionHandlers = [.. transmissionHandlers];
        _socketService = socketService;

        _packagedBuilder = new TransmissionPackageBuilder(OptionSendBufferSize);
    }

    protected override void Dispose(bool disposingManagedResources)
    {
        base.Dispose(disposingManagedResources);

        if (IsDisposed)
        {
            return;
        }

        _socketService.OnDeleted(this);
        MessageRecieved = null;
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        try
        {
            byte[] currentBuffer = buffer;
            long currentOffset = offset;
            long currentSize = size;

            while (true)
            {
                (bool IsComplete, byte[]? Data) package = _packagedBuilder.Receive(currentBuffer, currentOffset, currentSize);

                if (!package.IsComplete)
                {
                    return;
                }

                TransmissionWrapper transmissionWrapper = MessagePackSerializer.Deserialize<TransmissionWrapper>(package.Data);

                SocketTransmissionHandler? transmissionHandler = _transmissionHandlers.FirstOrDefault(x => x.HandlesType == transmissionWrapper.MessageType);

                if (transmissionHandler == null)
                {
                    _logger.LogWarning("No transmission handler found for type: {MessageType}", transmissionWrapper.MessageType);
                    return;
                }

                _logger.LogInformation("Recieved transmission: {TransmissionType}", transmissionWrapper.MessageType);

                SocketTransmissionHandler.Context context = new()
                {
                    SocketService = _socketService,
                    SocketClient = this
                };

                transmissionHandler.Execute(context, transmissionWrapper);

                if (_packagedBuilder.IsEmpty)
                {
                    return;
                }

                currentBuffer = [];
                currentOffset = 0;
                currentSize = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process received data in session {SessionId}", Id);
        }
    }

    public void SendTransmission<T>(T transmission) where T : Transmission
    {
        try
        {
            TransmissionWrapper wrapper = new()
            {
                MessageType = transmission.MessageType,
                Version = 1,
                Payload = MessagePackSerializer.Serialize(transmission)
            };

            byte[] data = MessagePackSerializer.Serialize(wrapper);
            IEnumerable<byte[]> packages = _packagedBuilder.Send(data);

            foreach (byte[] package in packages)
            {
                SendAsync(package);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send transmission!");
        }
    }

    public void AddRequest(IRequest request)
    {
        _requests.TryAdd(request, 0);
    }

    public void RemoveRequest(IRequest request)
    {
        _requests.TryRemove(request, out _);
    }

    public T[] GetRequests<T>() where T : IRequest
    {
        return [.. _requests.Keys.OfType<T>()];
    }

    public void OnMessageRecievedInternal(string? characterId, Message message)
    {
        MessageRecieved?.Invoke(characterId, message);
    }

    protected override void OnError(SocketError error)
    {
        _logger.LogError("Socket client caught an error with code {ErrorCode}", error);
    }
}
