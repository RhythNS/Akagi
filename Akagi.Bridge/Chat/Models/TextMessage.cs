using MessagePack;

namespace Akagi.Bridge.Chat.Models;

[MessagePackObject]
public class TextMessage
{
    public enum Type
    {
        User = 1,
        Character = 2,
        /// <summary>
        /// An instruction for the character (routines, consultations, call reports); the user does not see it.
        /// </summary>
        System = 4,
        /// <summary>
        /// The bot telling the user something directly, such as a command reply or an error. Never stored in a conversation.
        /// </summary>
        Notice = 8
    }

    [Key(0)]
    public DateTime Time { get; set; }
    [Key(1)]
    public Type From { get; set; }
    [Key(2)]
    public string Text { get; set; } = string.Empty;
}
