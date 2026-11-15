namespace Akagi.Web.Models.Chat;

public class Message
{
    [Flags]
    public enum Type
    {
        User = 1,
        Character = 2,
        /// <summary>
        /// An instruction for the character, such as a routine asking it to write first.
        /// </summary>
        System = 4,
        /// <summary>
        /// The bot telling the user something directly, such as a command reply.
        /// </summary>
        Notice = 8
    }

    /// <summary>
    /// The senders whose messages the chat shows. Instructions for the character stay out of it.
    /// </summary>
    public const Type ShownInChat = Type.User | Type.Character | Type.Notice;

    public static Type FromBridgeType(Bridge.Chat.Models.TextMessage.Type type)
    {
        return type switch
        {
            Bridge.Chat.Models.TextMessage.Type.User => Type.User,
            Bridge.Chat.Models.TextMessage.Type.Character => Type.Character,
            Bridge.Chat.Models.TextMessage.Type.System => Type.System,
            Bridge.Chat.Models.TextMessage.Type.Notice => Type.Notice,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown message type"),
        };
    }

    public static Bridge.Chat.Models.TextMessage.Type ToBridgeType(Type type)
    {
        return type switch
        {
            Type.User => Bridge.Chat.Models.TextMessage.Type.User,
            Type.Character => Bridge.Chat.Models.TextMessage.Type.Character,
            Type.System => Bridge.Chat.Models.TextMessage.Type.System,
            Type.Notice => Bridge.Chat.Models.TextMessage.Type.Notice,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown message type"),
        };
    }

    public required DateTime Time { get; set; }
    public required Type From { get; set; }
    /// <summary>The character (in a group: the member) who wrote it.</summary>
    public string? CharacterId { get; set; }
    /// <summary>Said inside a group and not to the user, such as a consultation and its answer.</summary>
    public bool Internal { get; set; }

    /// <summary>
    /// Whether it is a chat bubble. Everything else (tool calls, instructions, what members said to each other) is
    /// part of a turn's steps, shown when the turn is opened up.
    /// </summary>
    public virtual bool IsShownInChat => !Internal && (ShownInChat & From) == From;
}
