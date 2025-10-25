using MessagePack;

namespace Akagi.Bridge.Chat.Models;

[MessagePackObject]
public class Conversation
{
    [Key(0)]
    public int Id { get; set; } = -1;
    [Key(1)]
    public DateTime Time { get; set; } = DateTime.MinValue;
    [Key(2)]
    public List<TextMessage> Messages { get; set; } = [];
    [Key(3)]
    public bool IsCompleted { get; set; } = false;
}
