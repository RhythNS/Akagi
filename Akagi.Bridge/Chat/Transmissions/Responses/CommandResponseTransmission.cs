using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class CommandResponseTransmission : Transmission
{
    public override string MessageType => nameof(CommandResponseTransmission);

    [Key(0)]
    public Command[] Commands { get; set; } = [];
}
