using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class CommandRequestTransmission : Transmission
{
    public override string MessageType => nameof(CommandRequestTransmission);
}
