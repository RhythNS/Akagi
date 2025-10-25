using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class FileRequestTransmission : Transmission
{
    public override string MessageType => nameof(FileRequestTransmission);

    [Key(0)]
    public required string FileUrl { get; set; }
}
