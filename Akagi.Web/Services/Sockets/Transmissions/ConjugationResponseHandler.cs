using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class ConjugationResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(ConjugationResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        ConjugationResponseTransmission response = GetTransmission<ConjugationResponseTransmission>(transmissionWrapper);

        foreach (ConjugationRequest request in context.SocketClient.GetRequests<ConjugationRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
