using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class CommandResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(CommandResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        CommandResponseTransmission response = GetTransmission<CommandResponseTransmission>(transmissionWrapper);

        foreach (CommandListRequest request in context.SocketClient.GetRequests<CommandListRequest>())
        {
            request.Fulfill(response.Commands);
        }
    }
}
