namespace Akagi.Web.Models.Chat;

public class Conversation
{
    public int Id { get; set; } = -1;
    public DateTime Time { get; set; } = DateTime.MinValue;
    public List<Message> Messages { get; set; } = [];
    public bool IsCompleted { get; set; } = false;

    public static Conversation FromBridge(Bridge.Chat.Models.Conversation bridgeConversation)
    {
        List<Message> messages = [];
        foreach (Bridge.Chat.Models.TextMessage bridgeMessage in bridgeConversation.Messages)
        {
            if (bridgeMessage is Bridge.Chat.Models.TextMessage textMessage)
            {
                messages.Add(TextMessage.FromBridgeMessage(textMessage));
            }
            else
            {
                throw new NotSupportedException($"Unsupported message type: {bridgeMessage.GetType().Name}");
            }
        }

        return new Conversation
        {
            Id = bridgeConversation.Id,
            Time = bridgeConversation.Time,
            Messages = [.. messages.OrderBy(x => x.Time)],
            IsCompleted = bridgeConversation.IsCompleted
        };
    }
}
