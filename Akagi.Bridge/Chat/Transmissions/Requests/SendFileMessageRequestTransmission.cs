using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class SendFileMessageRequestTransmission : Transmission, IMessageRequest
{
    public override string MessageType => nameof(SendFileMessageRequestTransmission);
    [Key(0)]
    public required string CharacterId { get; set; }
    [Key(1)]
    public required string Text { get; set; }
    [Key(2)]
    public required string FileType { get; set; }
    [Key(3)]
    public required string FileName { get; set; }
    [Key(4)]
    public required byte[] FileData { get; set; }
}
