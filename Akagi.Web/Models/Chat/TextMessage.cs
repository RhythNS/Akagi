namespace Akagi.Web.Models.Chat;

public class TextMessage : Message
{
    public string Text { get; set; } = string.Empty;
    /// <summary>Set when this is a tool call rather than something said.</summary>
    public Bridge.Chat.Models.ToolCall? Tool { get; set; }

    public override bool IsShownInChat => Tool == null && base.IsShownInChat;

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
            Text = message.Text,
            CharacterId = message.CharacterId,
            Internal = message.Internal,
            Tool = message.Tool
        };
    }
}
