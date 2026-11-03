using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class LLMSettingsResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(LLMSettingsResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        LLMSettingsResponseTransmission response = GetTransmission<LLMSettingsResponseTransmission>(transmissionWrapper);

        foreach (LLMSettingsRequest request in context.SocketClient.GetRequests<LLMSettingsRequest>().Where(r => r.RequestId == response.RequestId))
        {
            request.Fulfill(response);
        }
    }
}
