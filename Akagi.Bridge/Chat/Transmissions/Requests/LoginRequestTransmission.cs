using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class LoginRequestTransmission : Transmission
{
    public override string MessageType => nameof(LoginRequestTransmission);

    [Key(0)]
    public required string Token { get; set; } = string.Empty;
}
