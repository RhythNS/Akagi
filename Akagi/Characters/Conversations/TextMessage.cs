using Akagi.Utils.Extensions;

namespace Akagi.Characters.Conversations;

internal class TextMessage : Message
{
    public string Text { get; set; } = string.Empty;

    public override string ToString() => $"{Time} {From}: {Text}";

    public const string SystemMessagePrefix = "[System] ";


    public override Message Copy()
    {
        return new TextMessage
        {
            Time = Time,
            From = From,
            Text = Text,
            CharacterId = CharacterId,
            Internal = Internal
        };
    }

    public override int CountWords() => Text.CountWords();
}
