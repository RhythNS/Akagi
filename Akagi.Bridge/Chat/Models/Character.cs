using MessagePack;

namespace Akagi.Bridge.Chat.Models;

[MessagePackObject]
public class Character
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string Name { get; set; } = string.Empty;
    [Key(2)]
    public string CardId { get; set; } = string.Empty;
    [Key(3)]
    public DateTime? LastMessageTime { get; set; }
}
