using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class KanbanResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(KanbanResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        KanbanResponseTransmission response = GetTransmission<KanbanResponseTransmission>(transmissionWrapper);

        foreach (KanbanRequest request in context.SocketClient.GetRequests<KanbanRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
