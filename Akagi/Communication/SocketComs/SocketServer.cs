using Microsoft.Extensions.Logging;
using NetCoreServer;
using System.Net;
using System.Net.Sockets;

namespace Akagi.Communication.SocketComs;

internal class SocketServer : TcpServer
{
    public SocketService SocketService { get; init; }

    private readonly ILogger<SocketServer> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public SocketServer(IPAddress address,
                        int port,
                        SocketService socketService,
                        ILoggerFactory loggerFactory) : base(address, port)
    {
        SocketService = socketService;

        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SocketServer>();
    }

    protected override TcpSession CreateSession()
    {
        return new SocketSession(this, _loggerFactory.CreateLogger<SocketSession>());
    }

    protected override void OnConnected(TcpSession session)
    {
        if (session is not SocketSession socketSession)
        {
            _logger.LogError("Session is not of type SocketSession: {SessionId}", session.Id);
            session.Disconnect();
            return;
        }

        _logger.LogInformation("Socket server connected: {SessionId} from {RemoteEndPoint}", socketSession.Id, socketSession.Socket.RemoteEndPoint);
        SocketService.AddSession(socketSession);
    }

    protected override void OnDisconnected(TcpSession session)
    {
        if (session is not SocketSession socketSession)
        {
            _logger.LogError("Session is not of type SocketSession: {SessionId}", session.Id);
            return;
        }

        _logger.LogInformation("Socket server disconnected: {SessionId}", socketSession.Id);
        SocketService.RemoveSession(socketSession);
    }

    protected override void OnError(SocketError error)
    {
        _logger.LogError("Socket server caught an error with code {ErrorCode}", error);
    }
}
