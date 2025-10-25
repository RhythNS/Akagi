using Akagi.Bridge.Chat.Transmissions;

namespace Akagi.Web.Services.Sockets;

public abstract class SocketTransmissionHandler : TransmissionHandler
{
    public class Context
    {
        public required SocketService SocketService { get; init; }
        public required SocketClient SocketClient { get; init; }
    }

    public abstract void Execute(Context context, TransmissionWrapper transmissionWrapper);
}
