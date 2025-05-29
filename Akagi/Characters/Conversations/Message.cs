namespace Akagi.Characters.Conversations;

internal abstract class Message
{
    public enum Type
    {
        User,
        Character,
        System
    }

    public required DateTime Time { get; set; }
    public required Type From { get; set; }

    public abstract bool IsVisible { get; }
}
