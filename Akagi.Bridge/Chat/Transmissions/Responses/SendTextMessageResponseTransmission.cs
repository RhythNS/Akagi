using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class SendTextMessageResponseTransmission : Transmission
{
    public override string MessageType => nameof(SendTextMessageResponseTransmission);
    [Key(0)]
    public required string CharacterId { get; set; }
    [Key(1)]
    public required string Text { get; set; }
    [Key(2)]
    public string? Error { get; set; } = null;
}
