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

    public required DateTime Time { get; set; }
    public required Type From { get; set; }
    public required Type VisibleTo { get; set; }
}
