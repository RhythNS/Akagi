using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class SendFileMessageResponseTransmission : Transmission
{
    public override string MessageType => nameof(SendFileMessageResponseTransmission);
    [Key(0)]
    public required string CharacterId { get; set; }
    [Key(1)]
    public required string Text { get; set; }
    [Key(2)]
    public string? FileType { get; set; }
    [Key(3)]
    public string? FileUrl { get; set; }
    [Key(4)]
    public string? Error { get; set; } = null;
}
