using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Reads and changes the sentence mining list and asks for its downloads. Responses are matched by id.
/// </summary>
public class MiningRequest : Request<MiningResponseTransmission, MiningRequestTransmission>
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Exports cut one clip per sentence or a whole call's audio on the server.
    /// </summary>
    public static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(5);

    private readonly MiningRequestTransmission _transmission;

    public MiningRequest(MiningRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    public MiningRequestTransmission Transmission => _transmission;

    protected override MiningRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}
