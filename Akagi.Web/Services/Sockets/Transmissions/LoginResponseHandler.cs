using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Services.Sockets.Transmissions;

public class LoginResponseHandler : SocketTransmissionHandler
{
    private readonly ILogger<LoginResponseHandler> _logger;

    public LoginResponseHandler(ILogger<LoginResponseHandler> logger)
    {
        _logger = logger;
    }

    public override string HandlesType => nameof(LoginResponseTransmission);

    public override void Execute(Context context, TransmissionWrapper transmissionWrapper)
    {
        LoginRequest[] requests = context.SocketClient.GetRequests<LoginRequest>();
        Array.ForEach(requests, request => request.Fulfill(true));
    }
}
