using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class TextMessageResponseTransmission : Transmission
{
    public override string MessageType => nameof(TextMessageResponseTransmission);

    [Key(0)]
    public string? CharacterId { get; set; } = string.Empty;

    [Key(1)]
    public required TextMessage Message { get; set; }
}
