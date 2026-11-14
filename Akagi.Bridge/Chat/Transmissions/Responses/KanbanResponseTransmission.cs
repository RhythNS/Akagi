using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class KanbanResponseTransmission : Transmission
{
    public override string MessageType => nameof(KanbanResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>Filled by <c>ListProjects</c>.</summary>
    [Key(1)]
    public KanbanProjectView[] Projects { get; set; } = [];
    /// <summary>The project a request was about, after the change.</summary>
    [Key(2)]
    public KanbanProjectView? Project { get; set; }
    /// <summary>All tickets of <see cref="Project"/>.</summary>
    [Key(3)]
    public KanbanTicketView[] Tickets { get; set; } = [];
    /// <summary>The ticket a request created or changed.</summary>
    [Key(4)]
    public string? TicketId { get; set; }
    /// <summary>
    /// Today in the user's time zone, which the plan starts from.
    /// </summary>
    [Key(5)]
    public DateTime Today { get; set; }
    [Key(6)]
    public string? Error { get; set; }
}
