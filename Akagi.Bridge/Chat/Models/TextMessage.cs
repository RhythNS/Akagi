using MessagePack;

namespace Akagi.Bridge.Chat.Models;

[MessagePackObject]
public class TextMessage
{
    public enum Type
    {
        User = 1,
        Character = 2,
        System = 4
    }

    [Key(0)]
    public DateTime Time { get; set; }
    [Key(1)]
    public Type From { get; set; }
    [Key(2)]
    public string Text { get; set; } = string.Empty;
}
