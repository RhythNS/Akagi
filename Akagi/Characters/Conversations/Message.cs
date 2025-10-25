namespace Akagi.Characters.Conversations;

internal abstract class Message
{
    [Flags]
    public enum Type
    {
        User = 1,
        Character = 2,
        System = 4
    }

    public static Type FromBridgeType(Bridge.Chat.Models.TextMessage.Type type)
    {
        return type switch
        {
            Bridge.Chat.Models.TextMessage.Type.User => Type.User,
            Bridge.Chat.Models.TextMessage.Type.Character => Type.Character,
            Bridge.Chat.Models.TextMessage.Type.System => Type.System,
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
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown message type"),
        };
    }

    public required DateTime Time { get; set; }
    public required Type From { get; set; }
    public required Type VisibleTo { get; set; }
}
