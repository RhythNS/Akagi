﻿namespace Akagi.Characters.Conversations;

internal class TextMessage : Message
{
    public string Text { get; set; } = string.Empty;

    public override string ToString() => $"{Time} {From}: {Text}";

    public const string SystemMessagePrefix = "[System] ";

    public Bridge.Chat.Models.TextMessage ToBridgeMessage()
    {
        return new Bridge.Chat.Models.TextMessage
        {
            Time = Time,
            From = ToBridgeType(From),
            Text = Text
        };
    }
}
