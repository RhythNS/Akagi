using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models.Chat;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class SendFileMessageHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(SendFileMessageResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        SendFileMessageResponseTransmission response = GetTransmission<SendFileMessageResponseTransmission>(transmissionWrapper);
        FileMessageResult result = new()
        {
            CharacterId = response.CharacterId,
            Message = response.Text,
            FileType = response.FileType,
            FileUrl = response.FileUrl,
            Error = response.Error
        };

        SendFileMessageRequest[] requests = context.SocketClient.GetRequests<SendFileMessageRequest>();
        foreach (SendFileMessageRequest request in requests)
        {
            if (request.CharacterId != result.CharacterId)
            {
                continue;
            }
            request.Fulfill(result);
        }
    }
}
