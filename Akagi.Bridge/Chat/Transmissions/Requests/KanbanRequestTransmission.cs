using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum KanbanAction
{
    /// <summary>All projects with their counts and finish day.</summary>
    ListProjects,
    /// <summary>One project with all of its tickets.</summary>
    Load,
    /// <summary>Only the project's <see cref="KanbanProjectView.UpdatedAt"/>, to tell whether to load again.</summary>
    Peek,
    /// <summary>Creates (empty id) or changes <see cref="KanbanRequestTransmission.Project"/>. Tickets of removed columns move to the first column of the same kind.</summary>
    SaveProject,
    DeleteProject,
    /// <summary>Creates (empty id) or changes the title, description, column, days and not-before day of <see cref="KanbanRequestTransmission.Ticket"/>.</summary>
    SaveTicket,
    DeleteTicket,
    /// <summary>Puts <see cref="KanbanRequestTransmission.TicketId"/> into <see cref="KanbanRequestTransmission.ColumnId"/>, ordered as <see cref="KanbanRequestTransmission.OrderedIds"/>.</summary>
    MoveTicket,
    /// <summary>Gives the tickets in <see cref="KanbanRequestTransmission.OrderedIds"/> their ranks in that order, keeping the ranks they had between them.</summary>
    Reorder,
    /// <summary>Adds <see cref="KanbanRequestTransmission.Text"/> as an update, optionally with new remaining days.</summary>
    AddUpdate,
    EditUpdate,
    DeleteUpdate,
    /// <summary><see cref="KanbanRequestTransmission.OtherTicketId"/> has to wait for <see cref="KanbanRequestTransmission.TicketId"/>.</summary>
    Link,
    Unlink,
    /// <summary>Sets days and not-before days of several tickets at once, see <see cref="KanbanRequestTransmission.Estimates"/>.</summary>
    Plan
}

/// <summary>
/// A change made while planning: null fields stay as they are.
/// </summary>
[MessagePackObject]
public class KanbanEstimateView
{
    [Key(0)]
    public string TicketId { get; set; } = string.Empty;
    [Key(1)]
    public double? RequiredDays { get; set; }
    [Key(2)]
    public double? RemainingDays { get; set; }
    /// <summary>Changes the not-before day when <see cref="SetNotBefore"/> is set; null clears it then.</summary>
    [Key(3)]
    public DateTime? NotBefore { get; set; }
    [Key(4)]
    public bool SetNotBefore { get; set; }
}

[MessagePackObject]
public class KanbanRequestTransmission : Transmission
{
    public override string MessageType => nameof(KanbanRequestTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public KanbanAction Action { get; set; }
    [Key(2)]
    public string ProjectId { get; set; } = string.Empty;
    [Key(3)]
    public KanbanProjectView? Project { get; set; }
    [Key(4)]
    public KanbanTicketView? Ticket { get; set; }
    [Key(5)]
    public string TicketId { get; set; } = string.Empty;
    [Key(6)]
    public string OtherTicketId { get; set; } = string.Empty;
    [Key(7)]
    public string ColumnId { get; set; } = string.Empty;
    [Key(8)]
    public string[] OrderedIds { get; set; } = [];
    [Key(9)]
    public string Text { get; set; } = string.Empty;
    [Key(10)]
    public string UpdateId { get; set; } = string.Empty;
    [Key(11)]
    public double? RemainingDays { get; set; }
    [Key(12)]
    public KanbanEstimateView[] Estimates { get; set; } = [];
}
