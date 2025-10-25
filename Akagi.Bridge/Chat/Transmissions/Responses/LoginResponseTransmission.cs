using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class LoginResponseTransmission : Transmission
{
    public override string MessageType => nameof(LoginResponseTransmission);
}
