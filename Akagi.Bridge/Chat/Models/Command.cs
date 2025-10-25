using MessagePack;

namespace Akagi.Bridge.Chat.Models;

[MessagePackObject]
public class Command
{
    [Key(0)]
    public required string Name { get; set; }

    [Key(1)]
    public required string Description { get; set; }
}
