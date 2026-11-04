using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Asks for the list of call analyses or everything one of them found. Responses are matched by id.
/// </summary>
public class CallAnalysisRequest : Request<CallAnalysisResponseTransmission, CallAnalysisRequestTransmission>
{
    /// <summary>
    /// A long call carries its whole transcript.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly CallAnalysisRequestTransmission _transmission;

    public CallAnalysisRequest(CallAnalysisRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    public CallAnalysisRequestTransmission Transmission => _transmission;

    protected override CallAnalysisRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}
