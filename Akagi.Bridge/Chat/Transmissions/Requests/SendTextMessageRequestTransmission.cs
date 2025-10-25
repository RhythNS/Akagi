using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class SendTextMessageRequestTransmission : Transmission
{
    public override string MessageType => nameof(SendTextMessageRequestTransmission);
    [Key(0)]
    public required string CharacterId { get; set; }
    [Key(1)]
    public required string Text { get; set; }
}
