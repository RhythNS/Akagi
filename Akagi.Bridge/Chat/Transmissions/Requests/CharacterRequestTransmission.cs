using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class CharacterRequestTransmission : Transmission
{
    public override string MessageType => nameof(CharacterRequestTransmission);
    [Key(0)]
    public required string[] Ids { get; set; }
}
