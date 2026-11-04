using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class VocabularyResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(VocabularyResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        VocabularyResponseTransmission response = GetTransmission<VocabularyResponseTransmission>(transmissionWrapper);

        foreach (VocabularyRequest request in context.SocketClient.GetRequests<VocabularyRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
