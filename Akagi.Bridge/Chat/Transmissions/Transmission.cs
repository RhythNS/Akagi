using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions;

public abstract class Transmission
{
    [IgnoreMember]
    public abstract string MessageType { get; }
}
