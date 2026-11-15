using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Web.Models.Chat;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>A chat's conversations, and whether a turn is running in it right now.</summary>
public record ConversationHistory(Conversation[] Conversations, bool IsBusy);

public class ConversationRequest : Request<ConversationHistory, ConversationRequestTransmission>
{
    public required string CharacterId { get; set; } = string.Empty;
    public int? ConversationId { get; set; } = null;

    protected override ConversationRequestTransmission GetTransmission()
    {
        return new()
        {
            CharacterId = CharacterId,
            ConversationId = ConversationId
        };
    }
}
