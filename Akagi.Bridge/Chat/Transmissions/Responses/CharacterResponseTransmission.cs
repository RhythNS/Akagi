using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class CharacterResponseTransmission : Transmission
{
    public override string MessageType => nameof(CharacterResponseTransmission);

    [Key(0)]
    public required Character[] Characters { get; set; } = [];

    [Key(1)]
    public required string[] RequestedIds { get; set; } = [];
}
