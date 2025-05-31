namespace Akagi.Characters.Conversations;

internal class TextMessage : Message
{
    public string Text { get; set; } = string.Empty;

    public override string ToString() => $"{Time} {From}: {Text}";
}
