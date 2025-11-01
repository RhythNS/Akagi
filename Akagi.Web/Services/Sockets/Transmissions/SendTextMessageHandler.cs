using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models.Chat;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class SendTextMessageHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(SendTextMessageResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        SendTextMessageResponseTransmission responseTransmission = GetTransmission<SendTextMessageResponseTransmission>(transmissionWrapper);
        TextMessageResult result = new()
        {
            CharacterId = responseTransmission.CharacterId,
            Text = responseTransmission.Text,
            Error = responseTransmission.Error
        };

        SendTextMessageRequest[] requests = context.SocketClient.GetRequests<SendTextMessageRequest>();
        foreach (SendTextMessageRequest request in requests)
        {
            if (request.CharacterId != responseTransmission.CharacterId)
            {
                continue;
            }
            request.Fulfill(result);
        }
    }
}
