using Akagi.Bridge.Chat.Models;

namespace Akagi.Kanban;

internal static class KanbanViews
{
    /// <summary>A day as stored and sent: midnight UTC.</summary>
    public static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static string KindName(KanbanColumnKind kind) => kind switch
    {
        KanbanColumnKind.Todo => "to do",
        KanbanColumnKind.InProgress => "in progress",
        KanbanColumnKind.Done => "done",
        _ => kind.ToString()
    };

    public static KanbanProjectView ToView(KanbanProject project) => new()
    {
        Id = project.Id ?? string.Empty,
        Name = project.Name,
        Description = project.Description,
        Columns = [.. project.Columns.Select(c => new KanbanColumnView { Id = c.Id, Name = c.Name, Kind = c.Kind })],
        StartDate = project.StartDate,
        WorkDays = project.WorkDays,
        Parallel = project.Parallel,
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt,
        Archived = project.Archived
    };

    public static KanbanTicketView ToView(KanbanTicket ticket) => new()
    {
        Id = ticket.Id ?? string.Empty,
        ProjectId = ticket.ProjectId,
        Number = ticket.Number,
        Title = ticket.Title,
        Description = ticket.Description,
        ColumnId = ticket.ColumnId,
        Rank = ticket.Rank,
        RequiredDays = ticket.RequiredDays,
        RemainingDays = ticket.RemainingDays,
        After = [.. ticket.After],
        NotBefore = ticket.NotBefore,
        Updates = [.. ticket.Updates.Select(u => new KanbanUpdateView
        {
            Id = u.Id,
            At = u.At,
            Author = u.Author,
            Text = u.Text,
            RemainingDays = u.RemainingDays
        })],
        CreatedAt = ticket.CreatedAt,
        UpdatedAt = ticket.UpdatedAt,
        StartedAt = ticket.StartedAt,
        DoneAt = ticket.DoneAt
    };
}
