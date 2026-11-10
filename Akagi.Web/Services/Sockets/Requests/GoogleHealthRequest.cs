using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Reads, renews or syncs the user's Google Health link. Responses are matched by id.
/// </summary>
public class GoogleHealthRequest : Request<GoogleHealthResponseTransmission, GoogleHealthRequestTransmission>
{
    /// <summary>
    /// Finishing a link exchanges the code and syncs several days, which takes a handful of Google calls.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly GoogleHealthRequestTransmission _transmission;

    public GoogleHealthRequest(GoogleHealthRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    public GoogleHealthRequestTransmission Transmission => _transmission;

    protected override GoogleHealthRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}
