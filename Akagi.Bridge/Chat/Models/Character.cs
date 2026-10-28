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
    /// <summary>
    /// The entry is a character group: messages sent to <see cref="Id"/> go to the group, and its members answer
    /// with their own ids (<see cref="MemberIds"/>). <see cref="CardId"/> is the card of the member that speaks.
    /// </summary>
    [Key(4)]
    public bool IsGroup { get; set; }
    [Key(5)]
    public string[] MemberIds { get; set; } = [];
    [Key(6)]
    public bool HasImage { get; set; }
}
