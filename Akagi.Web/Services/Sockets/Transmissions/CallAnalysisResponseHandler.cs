using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class CallAnalysisResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(CallAnalysisResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        CallAnalysisResponseTransmission response = GetTransmission<CallAnalysisResponseTransmission>(transmissionWrapper);

        foreach (CallAnalysisRequest request in context.SocketClient.GetRequests<CallAnalysisRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
