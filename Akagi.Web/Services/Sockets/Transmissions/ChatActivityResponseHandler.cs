using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class ChatActivityResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(ChatActivityResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        ChatActivityResponseTransmission activity = GetTransmission<ChatActivityResponseTransmission>(transmissionWrapper);
        Models.Chat.TextMessage? message = activity.Message == null ? null : Models.Chat.TextMessage.FromBridgeMessage(activity.Message);
        context.SocketClient.OnChatActivityInternal(activity.ChatId, activity.Kind, message);
    }
}
