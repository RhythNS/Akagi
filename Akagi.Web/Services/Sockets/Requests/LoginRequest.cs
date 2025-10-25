using Akagi.Bridge.Chat.Transmissions.Requests;

namespace Akagi.Web.Services.Sockets.Requests;

public class LoginRequest : Request<bool, LoginRequestTransmission>
{
    private readonly string _token;

    public LoginRequest(string token)
    {
        _token = token;
    }

    protected override LoginRequestTransmission GetTransmission()
    {
        return new()
        {
            Token = _token
        };
    }
}
