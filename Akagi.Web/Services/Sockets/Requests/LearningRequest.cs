using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// One step of a lesson. The server answers on its own time, so responses are matched by id.
/// </summary>
public class LearningRequest : Request<LearningResponseTransmission, LearningRequestTransmission>
{
    /// <summary>
    /// The tutor writes a whole lesson in one go.
    /// </summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan OverviewTimeout = TimeSpan.FromSeconds(20);

    private readonly LearningRequestTransmission _transmission;

    public LearningRequest(LearningRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    protected override LearningRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}
