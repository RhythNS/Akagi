using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class ConversationResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(ConversationResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        ConversationResponseTransmission characterResponseTransmission = GetTransmission<ConversationResponseTransmission>(transmissionWrapper);

        ConversationRequest[] conversationRequests = context.SocketClient.GetRequests<ConversationRequest>();

        Models.Chat.Conversation[] conversations = [.. characterResponseTransmission.Conversations.Select(Models.Chat.Conversation.FromBridge)];

        foreach (ConversationRequest request in conversationRequests)
        {
            if (request.CharacterId != characterResponseTransmission.CharacterId ||
                request.ConversationId != characterResponseTransmission.ConversationId)
            {
                continue;
            }

            request.Fulfill(conversations);
        }
    }
}
