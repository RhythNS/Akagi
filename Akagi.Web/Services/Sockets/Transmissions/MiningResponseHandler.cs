using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class MiningResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(MiningResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        MiningResponseTransmission response = GetTransmission<MiningResponseTransmission>(transmissionWrapper);

        foreach (MiningRequest request in context.SocketClient.GetRequests<MiningRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
