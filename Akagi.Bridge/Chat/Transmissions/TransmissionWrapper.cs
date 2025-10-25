using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions;

[MessagePackObject]
public class TransmissionWrapper
{
    [Key(0)]
    public string MessageType { get; set; } = string.Empty;

    [Key(1)]
    public int Version { get; set; } = 1;

    [Key(2)]
    public byte[]? Payload { get; set; }
}
