using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class GoogleHealthResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(GoogleHealthResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        GoogleHealthResponseTransmission response = GetTransmission<GoogleHealthResponseTransmission>(transmissionWrapper);

        foreach (GoogleHealthRequest request in context.SocketClient.GetRequests<GoogleHealthRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
