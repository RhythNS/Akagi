using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class StreamTextMessageResponseHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(StreamTextMessageResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        StreamTextMessageResponseTransmission response =
            GetTransmission<StreamTextMessageResponseTransmission>(transmissionWrapper);

        context.SocketClient.OnStreamChunkReceivedInternal(
            response.StreamId ?? string.Empty,
            response.CharacterId,
            response.TextDelta,
            response.IsComplete,
            response.FullText
        );
    }
}
