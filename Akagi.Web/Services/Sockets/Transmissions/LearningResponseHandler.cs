using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class LearningResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(LearningResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        LearningResponseTransmission response = GetTransmission<LearningResponseTransmission>(transmissionWrapper);

        foreach (LearningRequest request in context.SocketClient.GetRequests<LearningRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
