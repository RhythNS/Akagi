using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// What a column means for its tickets. Every project has at least one column of each kind.
/// </summary>
public enum KanbanColumnKind
{
    /// <summary>Work that has not started yet, e.g. "Backlog".</summary>
    Todo,
    /// <summary>Work that is being done, e.g. "Working" or "Review".</summary>
    InProgress,
    /// <summary>Finished work, including work that was dropped, e.g. "Done" or "Aborted".</summary>
    Done
}

[MessagePackObject]
public class KanbanColumnView
{
    /// <summary>
    /// Stable id inside its project; empty for a column that is about to be created.
    /// </summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string Name { get; set; } = string.Empty;
    [Key(2)]
    public KanbanColumnKind Kind { get; set; }

    public KanbanColumnView Copy() => new() { Id = Id, Name = Name, Kind = Kind };
}

/// <summary>
/// A project with its columns, in the order they are shown from left to right.
/// </summary>
[MessagePackObject]
public class KanbanProjectView
{
    /// <summary>Monday to Friday, see <see cref="WorkDays"/>.</summary>
    public const int WeekDays = (1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Tuesday) | (1 << (int)DayOfWeek.Wednesday)
                                | (1 << (int)DayOfWeek.Thursday) | (1 << (int)DayOfWeek.Friday);

    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// Markdown: what the project is about and what anyone working on it should know. Characters get it injected.
    /// </summary>
    [Key(2)]
    public string Description { get; set; } = string.Empty;
    [Key(3)]
    public KanbanColumnView[] Columns { get; set; } = [];
    /// <summary>
    /// The plan does not start before this day (date only); null starts it today.
    /// </summary>
    [Key(4)]
    public DateTime? StartDate { get; set; }
    /// <summary>
    /// The week days that count as work days, one bit per <see cref="DayOfWeek"/>.
    /// </summary>
    [Key(5)]
    public int WorkDays { get; set; } = WeekDays;
    /// <summary>
    /// How many tickets are worked on at the same time in the plan.
    /// </summary>
    [Key(6)]
    public int Parallel { get; set; } = 1;
    [Key(7)]
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Changes whenever the project or one of its tickets changes, so a page can tell when to load again.
    /// </summary>
    [Key(8)]
    public DateTime UpdatedAt { get; set; }
    [Key(9)]
    public bool Archived { get; set; }
    /// <summary>Filled in project lists: all tickets.</summary>
    [Key(10)]
    public int TicketCount { get; set; }
    /// <summary>Filled in project lists: tickets not in a done column.</summary>
    [Key(11)]
    public int OpenTickets { get; set; }
    /// <summary>Filled in project lists: remaining work days of the open tickets.</summary>
    [Key(12)]
    public double RemainingDays { get; set; }
    /// <summary>Filled in project lists: the day the plan finishes, null when nothing is open.</summary>
    [Key(13)]
    public DateTime? Finish { get; set; }

    public bool IsWorkDay(DayOfWeek day) => (WorkDays & (1 << (int)day)) != 0;

    public KanbanColumnView? Column(string? columnId) => Columns.FirstOrDefault(c => c.Id == columnId);

    public KanbanColumnKind KindOf(string? columnId) => Column(columnId)?.Kind ?? KanbanColumnKind.Todo;

    public KanbanProjectView Copy() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Columns = [.. Columns.Select(c => c.Copy())],
        StartDate = StartDate,
        WorkDays = WorkDays,
        Parallel = Parallel,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        Archived = Archived,
        TicketCount = TicketCount,
        OpenTickets = OpenTickets,
        RemainingDays = RemainingDays,
        Finish = Finish
    };
}

/// <summary>
/// A work item. Only the title shows on the board; the rest shows when it is opened.
/// </summary>
[MessagePackObject]
public class KanbanTicketView
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string ProjectId { get; set; } = string.Empty;
    /// <summary>
    /// Short number inside the project (#12), so people and models can name tickets.
    /// </summary>
    [Key(2)]
    public int Number { get; set; }
    [Key(3)]
    public string Title { get; set; } = string.Empty;
    /// <summary>Markdown: what needs to be done.</summary>
    [Key(4)]
    public string Description { get; set; } = string.Empty;
    [Key(5)]
    public string ColumnId { get; set; } = string.Empty;
    /// <summary>
    /// Priority: lower comes first, on the board inside a column and in the plan.
    /// </summary>
    [Key(6)]
    public double Rank { get; set; }
    /// <summary>The whole estimate in work days.</summary>
    [Key(7)]
    public double RequiredDays { get; set; }
    /// <summary>Work days still left; this is what the plan schedules.</summary>
    [Key(8)]
    public double RemainingDays { get; set; }
    /// <summary>
    /// Ids of the tickets that have to be finished before this one can start.
    /// </summary>
    [Key(9)]
    public string[] After { get; set; } = [];
    /// <summary>
    /// The plan does not start this ticket before this day (date only).
    /// </summary>
    [Key(10)]
    public DateTime? NotBefore { get; set; }
    /// <summary>Progress notes, oldest first.</summary>
    [Key(11)]
    public KanbanUpdateView[] Updates { get; set; } = [];
    [Key(12)]
    public DateTime CreatedAt { get; set; }
    [Key(13)]
    public DateTime UpdatedAt { get; set; }
    /// <summary>When it first went into an in progress column.</summary>
    [Key(14)]
    public DateTime? StartedAt { get; set; }
    /// <summary>When it last went into a done column.</summary>
    [Key(15)]
    public DateTime? DoneAt { get; set; }

    public string Reference => $"#{Number}";
}

/// <summary>
/// One progress note on a ticket.
/// </summary>
[MessagePackObject]
public class KanbanUpdateView
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public DateTime At { get; set; }
    /// <summary>Who wrote it: the user's name or a character's.</summary>
    [Key(2)]
    public string Author { get; set; } = string.Empty;
    /// <summary>Markdown.</summary>
    [Key(3)]
    public string Text { get; set; } = string.Empty;
    /// <summary>The remaining work days set together with this note, if any.</summary>
    [Key(4)]
    public double? RemainingDays { get; set; }
}
