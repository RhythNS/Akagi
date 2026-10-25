using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class StreamTextMessageResponseTransmission : Transmission
{
    public override string MessageType => nameof(StreamTextMessageResponseTransmission);

    [Key(0)]
    public string? StreamId { get; set; } = string.Empty;

    [Key(1)]
    public string? CharacterId { get; set; } = string.Empty;

    [Key(2)]
    public string TextDelta { get; set; } = string.Empty;

    [Key(3)]
    public bool IsComplete { get; set; }

    [Key(4)]
    public string? FullText { get; set; }
}
