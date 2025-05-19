namespace Akagi.Characters;

internal class TextMessage : Message
{
    public string Text { get; set; } = string.Empty;

    public override bool IsVisible => true;
}
