namespace Akagi.Web.Models.Chat;

public class TextMessage : Message
{
    public string Text { get; set; } = string.Empty;

    public Bridge.Chat.Models.TextMessage ToBridgeMessage()
    {
        return new Bridge.Chat.Models.TextMessage
        {
            Time = Time,
            From = ToBridgeType(From),
            Text = Text
        };
    }

    public static TextMessage FromBridgeMessage(Bridge.Chat.Models.TextMessage message)
    {
        return new TextMessage
        {
            Time = message.Time,
            From = FromBridgeType(message.From),
            Text = message.Text
        };
    }
}
