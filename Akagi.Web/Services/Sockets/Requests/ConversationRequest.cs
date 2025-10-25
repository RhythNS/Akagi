using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Web.Models.Chat;

namespace Akagi.Web.Services.Sockets.Requests;

public class ConversationRequest : Request<Conversation[], ConversationRequestTransmission>
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
