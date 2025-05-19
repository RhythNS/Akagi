namespace Akagi.Characters;

internal abstract class Message
{
    public enum Type
    {
        User,
        Character,
        System
    }

    public DateTime Time { get; set; }
    public Type From { get; set; } = Type.User;

    public abstract bool IsVisible { get; }
}
