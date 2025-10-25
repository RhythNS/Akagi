using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class CharacterResponseHandler : SocketTransmissionHandler
{
    private readonly ILogger<CharacterResponseHandler> _logger;

    public CharacterResponseHandler(ILogger<CharacterResponseHandler> logger)
    {
        _logger = logger;
    }

    public override string HandlesType => nameof(CharacterResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        CharacterResponseTransmission characterResponseTransmission = GetTransmission<CharacterResponseTransmission>(transmissionWrapper);

        CharacterListRequest[] requests = context.SocketClient.GetRequests<CharacterListRequest>();

        foreach (CharacterListRequest request in requests)
        {
            if (request.Ids.Except(characterResponseTransmission.RequestedIds).Any())
            {
                continue;
            }
            request.Fulfill([.. characterResponseTransmission.Characters.Select(Models.Chat.Character.FromBridge)]);
        }
    }
}
