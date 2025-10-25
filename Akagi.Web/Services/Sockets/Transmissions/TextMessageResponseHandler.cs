using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class TextMessageResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(TextMessageResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        TextMessageResponseTransmission responseTransmission = GetTransmission<TextMessageResponseTransmission>(transmissionWrapper);

        context.SocketClient.OnMessageRecievedInternal(responseTransmission.CharacterId, Models.Chat.TextMessage.FromBridgeMessage(responseTransmission.Message));
    }
}
