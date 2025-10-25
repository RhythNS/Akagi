using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class FileResponseTransmission : Transmission
{
    public override string MessageType => nameof(FileResponseTransmission);

    [Key(0)]
    public required string FileUrl { get; set; }

    [Key(1)]
    public required string Type { get; set; } = "image";

    [Key(2)]
    public required byte[] Data { get; set; } = [];

    [Key(3)]
    public string? Error { get; set; }
}
