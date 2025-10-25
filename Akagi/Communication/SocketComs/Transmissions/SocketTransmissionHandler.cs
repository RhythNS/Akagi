using Akagi.Bridge.Chat.Transmissions;
using Akagi.Data;
using Akagi.Users;

namespace Akagi.Communication.SocketComs.Transmissions;

internal abstract class SocketTransmissionHandler : TransmissionHandler
{
    public class Context : ContextBase
    {
        public required SocketService Service { get; init; }
        public required SocketSession Session { get; init; }
        public User? User => Session.User;

        protected override Savable?[] ToTrack => [];
    }

    public abstract Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper);
}
