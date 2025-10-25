using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class ImageResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(FileResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        FileResponseTransmission imageResponseTransmission = GetTransmission<FileResponseTransmission>(transmissionWrapper);

        FileRequest[] requests = context.SocketClient.GetRequests<FileRequest>();
        foreach (FileRequest request in requests)
        {
            if (string.Equals(request.FileUrl, imageResponseTransmission.FileUrl) == false)
            {
                continue;
            }

            request.Fulfill(SocketFile.FromBridgeTransmission(imageResponseTransmission));
        }
    }
}
