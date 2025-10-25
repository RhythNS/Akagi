using Akagi.Bridge.Chat.Transmissions;
using Akagi.Users;
using MessagePack;
using Microsoft.Extensions.Logging;
using NetCoreServer;
using System.Net.Sockets;

namespace Akagi.Communication.SocketComs;

internal class SocketSession : TcpSession
{
    public User? User { get; set; }

    private readonly SocketServer _socketServer;
    private readonly TransmissionPackageBuilder _packagedBuilder;
    private readonly ILogger<SocketSession> _logger;

    private Timer? _registrationTimer;

    public SocketSession(SocketServer server,
                         ILogger<SocketSession> logger) : base(server)
    {
        _socketServer = server;
        _logger = logger;

        _packagedBuilder = new TransmissionPackageBuilder(server.OptionSendBufferSize);
    }

    protected override void OnConnected()
    {
        _registrationTimer = new Timer(CheckRegistration, null, 5 * 1000, Timeout.Infinite);
    }

    protected override void OnDisconnected()
    {
        _registrationTimer?.Dispose();
        _registrationTimer = null;

        if (User != null)
        {
            _logger.LogInformation("User {UserId} disconnected from session {SessionId}", User.Id, Id);
        }
        else
        {
            _logger.LogInformation("Session {Id} disconnected without a registered user.", Id);
        }
    }

    private void CheckRegistration(object? state)
    {
        if (User != null)
        {
            return;
        }

        _logger?.LogInformation("Session did not register. Disconnecting.");
        Disconnect();
    }

    public void SendTransmission<T>(T transmission) where T : Transmission
    {
        try
        {
            _logger.LogInformation("Sending transmission {transmission} to {SessionId}", transmission.MessageType, Id);

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
            _logger.LogError(ex, "Failed to send transmission to session {SessionId}", Id);
        }
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
                (bool IsComplete, byte[]? Data) package = _packagedBuilder.Receive(buffer, offset, size);
                if (!package.IsComplete)
                {
                    return;
                }

                TransmissionWrapper transmissionWrapper = MessagePackSerializer.Deserialize<TransmissionWrapper>(package.Data);

                bool result = _socketServer.SocketService.RecieveTransmission(this, transmissionWrapper).GetAwaiter().GetResult();
                if (result == false)
                {
                    _logger.LogWarning("Failed to process transmission in session {SessionId}", Id);
                    Disconnect();
                }

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
            Disconnect();
        }
    }

    protected override void OnError(SocketError error)
    {
        _logger.LogError("Socket session caught an error with code {ErrorCode}", error);
    }
}
