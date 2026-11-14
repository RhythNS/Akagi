using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;

namespace Akagi.Web.Services.Sockets.Requests;

/// <summary>
/// Reads and changes Kanban projects and tickets. Responses are matched by id.
/// </summary>
public class KanbanRequest : Request<KanbanResponseTransmission, KanbanRequestTransmission>
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly KanbanRequestTransmission _transmission;

    public KanbanRequest(KanbanRequestTransmission transmission)
    {
        _transmission = transmission;
        _transmission.RequestId = Guid.NewGuid().ToString("N");
    }

    public string RequestId => _transmission.RequestId;

    public KanbanRequestTransmission Transmission => _transmission;

    protected override KanbanRequestTransmission GetTransmission()
    {
        return _transmission;
    }
}
