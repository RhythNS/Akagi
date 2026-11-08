using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Loads the conjugation practice, records answers and keeps its settings. Responses are matched by id.
/// </summary>
public class ConjugationRequest : Request<ConjugationResponseTransmission, ConjugationRequestTransmission>
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ConjugationRequestTransmission _transmission;

    public ConjugationRequest(ConjugationRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    public ConjugationRequestTransmission Transmission => _transmission;

    protected override ConjugationRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}
