using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Kanban;

namespace Akagi.Kanban;

/// <summary>
/// A change the user can read: the project or ticket does not exist, or the change breaks a rule.
/// </summary>
internal class KanbanException : Exception
{
    public KanbanException(string message) : base(message)
    {
    }
}

/// <summary>
/// Changes to a ticket; null fields stay as they are.
/// </summary>
internal sealed class KanbanTicketChange
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? ColumnId { get; init; }
    public double? RequiredDays { get; init; }
    public double? RemainingDays { get; init; }
    /// <summary>Changes the not-before day when set; <see cref="NotBefore"/> null then clears it.</summary>
    public bool SetNotBefore { get; init; }
    public DateTime? NotBefore { get; init; }
}

/// <summary>
/// A project with all of its tickets, as loaded for one request.
/// </summary>
internal sealed class KanbanBoard
{
    public required KanbanProject Project { get; init; }
    public required List<KanbanTicket> Tickets { get; init; }

    public KanbanProjectView ProjectView() => KanbanViews.ToView(Project);

    public KanbanTicketView[] TicketViews() => [.. Tickets.Select(KanbanViews.ToView)];

    public KanbanPlan Plan(DateOnly today) => KanbanScheduler.Plan(ProjectView(), TicketViews(), today);

    public KanbanTicket? Ticket(string id) => Tickets.FirstOrDefault(t => t.Id == id);

    /// <summary>Tickets in board order: by column from left to right, then by rank.</summary>
    public IEnumerable<KanbanTicket> InColumn(string columnId) =>
        Tickets.Where(t => t.ColumnId == columnId).OrderBy(t => t.Rank).ThenBy(t => t.Number);
}

/// <summary>
/// Everything that reads or changes projects and tickets, shared by the website and the characters' tools.
/// </summary>
internal class KanbanService
{
    public const double MaxDays = 1000;
    public const int MaxTitle = 200;
    public const int MaxColumns = 20;

    private readonly IKanbanProjectDatabase _projects;
    private readonly IKanbanTicketDatabase _tickets;
    private readonly Func<DateTime> _clock;

    public KanbanService(IKanbanProjectDatabase projects, IKanbanTicketDatabase tickets, Func<DateTime>? clock = null)
    {
        _projects = projects;
        _tickets = tickets;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public static IReadOnlyList<KanbanColumn> DefaultColumns() =>
    [
        new KanbanColumn { Name = "Backlog", Kind = KanbanColumnKind.Todo },
        new KanbanColumn { Name = "In progress", Kind = KanbanColumnKind.InProgress },
        new KanbanColumn { Name = "Done", Kind = KanbanColumnKind.Done }
    ];

    public async Task<List<KanbanProject>> ProjectsAsync(string userId) =>
        [.. (await _projects.GetForUser(userId)).OrderBy(p => p.Archived).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];

    public async Task<KanbanBoard> LoadAsync(string userId, string projectId)
    {
        KanbanProject project = await ProjectAsync(userId, projectId);
        return new KanbanBoard { Project = project, Tickets = await _tickets.GetForProject(project.Id!) };
    }

    /// <summary>
    /// The projects with their counts and the day each plan finishes.
    /// </summary>
    public async Task<List<KanbanProjectView>> SummariesAsync(string userId, DateOnly today)
    {
        List<KanbanProjectView> views = [];
        foreach (KanbanProject project in await ProjectsAsync(userId))
        {
            KanbanBoard board = new() { Project = project, Tickets = await _tickets.GetForProject(project.Id!) };
            views.Add(Summary(board, today));
        }
        return views;
    }

    public static KanbanProjectView Summary(KanbanBoard board, DateOnly today)
    {
        KanbanProjectView view = board.ProjectView();
        KanbanPlan plan = board.Plan(today);
        view.TicketCount = board.Tickets.Count;
        view.OpenTickets = board.Tickets.Count(t => board.Project.KindOf(t.ColumnId) != KanbanColumnKind.Done);
        view.RemainingDays = plan.RemainingDays;
        view.Finish = plan.FinishDate is DateOnly finish ? KanbanViews.Day(finish) : null;
        return view;
    }

    // ---- Projects ----

    public async Task<KanbanProject> CreateProjectAsync(string userId, string name, string description = "", IEnumerable<KanbanColumn>? columns = null)
    {
        KanbanProject project = new()
        {
            UserId = userId,
            Name = CleanName(name, "project"),
            Description = description?.Trim() ?? string.Empty,
            Columns = [.. (columns ?? DefaultColumns()).Select(c => new KanbanColumn { Id = c.Id, Name = c.Name.Trim(), Kind = c.Kind })],
            CreatedAt = _clock(),
            UpdatedAt = _clock()
        };
        CheckColumns(project.Columns);
        await CheckUniqueName(userId, project.Name, null);
        await _projects.SaveDocumentAsync(project);
        return project;
    }

    /// <summary>
    /// Creates the project when the view has no id, otherwise changes it. Tickets in removed columns move to the first
    /// remaining column of the same kind.
    /// </summary>
    public async Task<KanbanBoard> SaveProjectAsync(string userId, KanbanProjectView view)
    {
        List<KanbanColumn> columns = [.. view.Columns.Select(c => new KanbanColumn
        {
            Id = string.IsNullOrWhiteSpace(c.Id) ? KanbanColumn.NewId() : c.Id,
            Name = c.Name?.Trim() ?? string.Empty,
            Kind = c.Kind
        })];
        if (string.IsNullOrEmpty(view.Id))
        {
            KanbanProject created = await CreateProjectAsync(userId, view.Name, view.Description, columns);
            ApplySettings(created, view);
            await _projects.SaveDocumentAsync(created);
            return new KanbanBoard { Project = created, Tickets = [] };
        }

        KanbanBoard board = await LoadAsync(userId, view.Id);
        KanbanProject project = board.Project;
        string name = CleanName(view.Name, "project");
        if (!string.Equals(name, project.Name, StringComparison.Ordinal))
        {
            await CheckUniqueName(userId, name, project.Id);
        }
        CheckColumns(columns);

        DateTime now = _clock();
        foreach (KanbanTicket ticket in board.Tickets.Where(t => columns.All(c => c.Id != t.ColumnId)))
        {
            KanbanColumnKind oldKind = project.KindOf(ticket.ColumnId);
            KanbanColumn target = columns.First(c => c.Kind == oldKind);
            ticket.ColumnId = target.Id;
            ticket.UpdatedAt = now;
            await _tickets.SaveDocumentAsync(ticket);
        }

        project.Name = name;
        project.Description = view.Description?.Trim() ?? string.Empty;
        project.Columns = columns;
        project.Archived = view.Archived;
        ApplySettings(project, view);
        await TouchAsync(project);
        return board;
    }

    public async Task DeleteProjectAsync(string userId, string projectId)
    {
        KanbanProject project = await ProjectAsync(userId, projectId);
        await _tickets.DeleteForProject(project.Id!);
        await _projects.DeleteDocumentByIdAsync(project.Id!);
    }

    private static void ApplySettings(KanbanProject project, KanbanProjectView view)
    {
        project.StartDate = view.StartDate is DateTime start ? KanbanViews.Day(DateOnly.FromDateTime(start)) : null;
        project.WorkDays = (view.WorkDays & 0x7F) == 0 ? KanbanProjectView.WeekDays : view.WorkDays & 0x7F;
        project.Parallel = Math.Clamp(view.Parallel, 1, 50);
    }

    private static void CheckColumns(List<KanbanColumn> columns)
    {
        if (columns.Count > MaxColumns)
        {
            throw new KanbanException($"A project can have at most {MaxColumns} columns.");
        }
        if (columns.Any(c => string.IsNullOrWhiteSpace(c.Name)))
        {
            throw new KanbanException("Every column needs a name.");
        }
        if (columns.Any(c => !Enum.IsDefined(c.Kind)))
        {
            throw new KanbanException("A column has an unknown type.");
        }
        if (columns.Select(c => c.Id).Distinct().Count() != columns.Count)
        {
            throw new KanbanException("Two columns have the same id.");
        }
        foreach (KanbanColumnKind kind in Enum.GetValues<KanbanColumnKind>())
        {
            if (columns.All(c => c.Kind != kind))
            {
                throw new KanbanException($"A project needs at least one {KanbanViews.KindName(kind)} column.");
            }
        }
    }

    private async Task CheckUniqueName(string userId, string name, string? exceptId)
    {
        if ((await _projects.GetForUser(userId)).Any(p => p.Id != exceptId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new KanbanException($"There already is a project called '{name}'.");
        }
    }

    // ---- Tickets ----

    public async Task<KanbanTicket> CreateTicketAsync(string userId, string projectId, string title, string? description = null,
        string? columnId = null, double requiredDays = 0, double? remainingDays = null, IEnumerable<string>? after = null, DateTime? notBefore = null)
    {
        KanbanBoard board = await LoadAsync(userId, projectId);
        KanbanProject project = board.Project;
        KanbanColumn column = string.IsNullOrEmpty(columnId)
            ? project.FirstOf(KanbanColumnKind.Todo)
            : project.Column(columnId) ?? throw new KanbanException("That column does not exist in this project.");

        DateTime now = _clock();
        KanbanTicket ticket = new()
        {
            UserId = userId,
            ProjectId = project.Id!,
            Number = project.NextNumber++,
            Title = CleanName(title, "ticket"),
            Description = description?.Trim() ?? string.Empty,
            ColumnId = project.FirstOf(KanbanColumnKind.Todo).Id,
            Rank = board.Tickets.Count == 0 ? 0 : board.Tickets.Max(t => t.Rank) + 1,
            RequiredDays = CheckDays(requiredDays),
            RemainingDays = CheckDays(remainingDays ?? requiredDays),
            NotBefore = notBefore is DateTime day ? KanbanViews.Day(DateOnly.FromDateTime(day)) : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        EnterColumn(ticket, project, column, now);

        foreach (string id in after ?? [])
        {
            if (board.Ticket(id) == null)
            {
                throw new KanbanException("A ticket it should wait for is not in this project.");
            }
            if (!ticket.After.Contains(id))
            {
                ticket.After.Add(id);
            }
        }

        await _tickets.SaveDocumentAsync(ticket);
        await TouchAsync(project);
        return ticket;
    }

    public async Task<(KanbanBoard Board, KanbanTicket Ticket)> UpdateTicketAsync(string userId, string ticketId, KanbanTicketChange change)
    {
        (KanbanBoard board, KanbanTicket ticket) = await TicketAsync(userId, ticketId);
        KanbanProject project = board.Project;
        DateTime now = _clock();

        if (change.Title != null)
        {
            ticket.Title = CleanName(change.Title, "ticket");
        }
        if (change.Description != null)
        {
            ticket.Description = change.Description.Trim();
        }
        if (change.ColumnId != null && change.ColumnId != ticket.ColumnId)
        {
            KanbanColumn column = project.Column(change.ColumnId) ?? throw new KanbanException("That column does not exist in this project.");
            EnterColumn(ticket, project, column, now);
        }
        SetEstimate(ticket, project, change.RequiredDays, change.RemainingDays);
        if (change.SetNotBefore)
        {
            ticket.NotBefore = change.NotBefore is DateTime day ? KanbanViews.Day(DateOnly.FromDateTime(day)) : null;
        }

        ticket.UpdatedAt = now;
        await _tickets.SaveDocumentAsync(ticket);
        await TouchAsync(project);
        return (board, ticket);
    }

    /// <summary>
    /// Moves the ticket into the column; <paramref name="orderedIds"/>, when given, is the column's new order.
    /// </summary>
    public async Task<(KanbanBoard Board, KanbanTicket Ticket)> MoveTicketAsync(string userId, string ticketId, string columnId, IReadOnlyList<string>? orderedIds = null)
    {
        (KanbanBoard board, KanbanTicket ticket) = await TicketAsync(userId, ticketId);
        KanbanColumn column = board.Project.Column(columnId) ?? throw new KanbanException("That column does not exist in this project.");
        DateTime now = _clock();
        if (ticket.ColumnId != column.Id)
        {
            EnterColumn(ticket, board.Project, column, now);
            ticket.UpdatedAt = now;
            if (orderedIds == null || orderedIds.Count == 0)
            {
                // At the bottom of its new column
                ticket.Rank = board.Tickets.Max(t => t.Rank) + 1;
            }
        }
        if (orderedIds is { Count: > 0 })
        {
            Reorder(board, orderedIds);
            await SaveAllAsync(board);
        }
        else
        {
            await _tickets.SaveDocumentAsync(ticket);
        }
        await TouchAsync(board.Project);
        return (board, ticket);
    }

    /// <summary>
    /// Gives the listed tickets the places they had between them, in the listed order. Other tickets keep theirs.
    /// </summary>
    public async Task<KanbanBoard> ReorderAsync(string userId, string projectId, IReadOnlyList<string> orderedIds)
    {
        KanbanBoard board = await LoadAsync(userId, projectId);
        Reorder(board, orderedIds);
        await SaveAllAsync(board);
        await TouchAsync(board.Project);
        return board;
    }

    private static void Reorder(KanbanBoard board, IReadOnlyList<string> orderedIds)
    {
        List<string> ids = [.. orderedIds.Distinct()];
        if (ids.Any(id => board.Ticket(id) == null))
        {
            throw new KanbanException("A ticket in that order is not in this project.");
        }
        List<KanbanTicket> order = [.. board.Tickets.OrderBy(t => t.Rank).ThenBy(t => t.Number)];
        HashSet<string> moving = [.. ids];
        Queue<KanbanTicket> next = new(ids.Select(id => board.Ticket(id)!));
        for (int i = 0; i < order.Count; i++)
        {
            if (moving.Contains(order[i].Id!))
            {
                order[i] = next.Dequeue();
            }
        }
        for (int i = 0; i < order.Count; i++)
        {
            order[i].Rank = i;
        }
    }

    public async Task<KanbanBoard> DeleteTicketAsync(string userId, string ticketId)
    {
        (KanbanBoard board, KanbanTicket ticket) = await TicketAsync(userId, ticketId);
        await _tickets.DeleteDocumentByIdAsync(ticket.Id!);
        board.Tickets.Remove(ticket);
        foreach (KanbanTicket other in board.Tickets.Where(t => t.After.Remove(ticket.Id!)))
        {
            await _tickets.SaveDocumentAsync(other);
        }
        await TouchAsync(board.Project);
        return board;
    }

    // ---- Updates ----

    public async Task<(KanbanBoard Board, KanbanTicket Ticket)> AddUpdateAsync(string userId, string ticketId, string author, string text, double? remainingDays = null)
    {
        if (string.IsNullOrWhiteSpace(text) && remainingDays == null)
        {
            throw new KanbanException("An update needs some text.");
        }
        (KanbanBoard board, KanbanTicket ticket) = await TicketAsync(userId, ticketId);
        DateTime now = _clock();
        if (remainingDays != null)
        {
            SetEstimate(ticket, board.Project, null, remainingDays);
        }
        ticket.Updates.Add(new KanbanUpdate
        {
            At = now,
            Author = author.Trim(),
            Text = text?.Trim() ?? string.Empty,
            RemainingDays = remainingDays == null ? null : ticket.RemainingDays
        });
        ticket.UpdatedAt = now;
        await _tickets.SaveDocumentAsync(ticket);
        await TouchAsync(board.Project);
        return (board, ticket);
    }

    public async Task<(KanbanBoard Board, KanbanTicket Ticket)> EditUpdateAsync(string userId, string ticketId, string updateId, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new KanbanException("An update needs some text; delete it instead.");
        }
        (KanbanBoard board, KanbanTicket ticket) = await TicketAsync(userId, ticketId);
        KanbanUpdate update = ticket.Updates.FirstOrDefault(u => u.Id == updateId) ?? throw new KanbanException("That update does not exist.");
        update.Text = text.Trim();
        ticket.UpdatedAt = _clock();
        await _tickets.SaveDocumentAsync(ticket);
        await TouchAsync(board.Project);
        return (board, ticket);
    }

    public async Task<(KanbanBoard Board, KanbanTicket Ticket)> DeleteUpdateAsync(string userId, string ticketId, string updateId)
    {
        (KanbanBoard board, KanbanTicket ticket) = await TicketAsync(userId, ticketId);
        if (ticket.Updates.RemoveAll(u => u.Id == updateId) == 0)
        {
            throw new KanbanException("That update does not exist.");
        }
        ticket.UpdatedAt = _clock();
        await _tickets.SaveDocumentAsync(ticket);
        await TouchAsync(board.Project);
        return (board, ticket);
    }

    // ---- Links ----

    /// <summary>
    /// <paramref name="laterId"/> waits until <paramref name="firstId"/> is finished.
    /// </summary>
    public async Task<KanbanBoard> LinkAsync(string userId, string firstId, string laterId)
    {
        (KanbanBoard board, KanbanTicket later) = await TicketAsync(userId, laterId);
        KanbanTicket first = board.Ticket(firstId) ?? throw new KanbanException("Both tickets have to be in the same project.");
        if (first.Id == later.Id)
        {
            throw new KanbanException("A ticket cannot wait for itself.");
        }
        if (later.After.Contains(first.Id!))
        {
            return board;
        }
        if (WaitsFor(board, first, later.Id!))
        {
            throw new KanbanException($"{first.Reference} already waits for {later.Reference}, so {later.Reference} cannot wait for {first.Reference}.");
        }
        later.After.Add(first.Id!);
        later.UpdatedAt = _clock();
        await _tickets.SaveDocumentAsync(later);
        await TouchAsync(board.Project);
        return board;
    }

    public async Task<KanbanBoard> UnlinkAsync(string userId, string firstId, string laterId)
    {
        (KanbanBoard board, KanbanTicket later) = await TicketAsync(userId, laterId);
        if (later.After.Remove(firstId))
        {
            later.UpdatedAt = _clock();
            await _tickets.SaveDocumentAsync(later);
            await TouchAsync(board.Project);
        }
        return board;
    }

    /// <summary>
    /// Whether <paramref name="ticket"/> waits for <paramref name="otherId"/>, directly or through other tickets.
    /// </summary>
    public static bool WaitsFor(KanbanBoard board, KanbanTicket ticket, string otherId)
    {
        HashSet<string> seen = [];
        Stack<KanbanTicket> stack = new([ticket]);
        while (stack.Count > 0)
        {
            KanbanTicket current = stack.Pop();
            foreach (string id in current.After)
            {
                if (id == otherId)
                {
                    return true;
                }
                if (seen.Add(id) && board.Ticket(id) is KanbanTicket before)
                {
                    stack.Push(before);
                }
            }
        }
        return false;
    }

    // ---- Planning ----

    public async Task<KanbanBoard> PlanAsync(string userId, string projectId, IEnumerable<KanbanEstimateView> estimates)
    {
        KanbanBoard board = await LoadAsync(userId, projectId);
        DateTime now = _clock();
        List<KanbanTicket> changed = [];
        foreach (KanbanEstimateView estimate in estimates)
        {
            KanbanTicket ticket = board.Ticket(estimate.TicketId) ?? throw new KanbanException("A planned ticket is not in this project.");
            SetEstimate(ticket, board.Project, estimate.RequiredDays, estimate.RemainingDays);
            if (estimate.SetNotBefore)
            {
                ticket.NotBefore = estimate.NotBefore is DateTime day ? KanbanViews.Day(DateOnly.FromDateTime(day)) : null;
            }
            ticket.UpdatedAt = now;
            changed.Add(ticket);
        }
        foreach (KanbanTicket ticket in changed.Distinct())
        {
            await _tickets.SaveDocumentAsync(ticket);
        }
        await TouchAsync(board.Project);
        return board;
    }

    // ---- Rules ----

    /// <summary>
    /// Sets the estimate. Before work starts the remaining days follow the required days, as long as they were the same.
    /// </summary>
    internal static void SetEstimate(KanbanTicket ticket, KanbanProject project, double? required, double? remaining)
    {
        if (required is double newRequired)
        {
            newRequired = CheckDays(newRequired);
            bool follows = remaining == null
                && project.KindOf(ticket.ColumnId) == KanbanColumnKind.Todo
                && Math.Abs(ticket.RemainingDays - ticket.RequiredDays) < 1e-9;
            ticket.RequiredDays = newRequired;
            if (follows)
            {
                ticket.RemainingDays = newRequired;
            }
        }
        if (remaining is double newRemaining)
        {
            ticket.RemainingDays = CheckDays(newRemaining);
        }
    }

    /// <summary>
    /// Puts the ticket into the column and notes when work started and finished.
    /// </summary>
    internal static void EnterColumn(KanbanTicket ticket, KanbanProject project, KanbanColumn column, DateTime now)
    {
        KanbanColumnKind before = project.KindOf(ticket.ColumnId);
        bool known = project.Column(ticket.ColumnId) != null;
        ticket.ColumnId = column.Id;
        switch (column.Kind)
        {
            case KanbanColumnKind.InProgress:
                ticket.StartedAt ??= now;
                break;
            case KanbanColumnKind.Done when before != KanbanColumnKind.Done || !known:
                ticket.DoneAt = now;
                ticket.RemainingDays = 0;
                break;
        }
        if (column.Kind != KanbanColumnKind.Done && before == KanbanColumnKind.Done && known)
        {
            ticket.DoneAt = null;
            if (ticket.RemainingDays <= 0)
            {
                ticket.RemainingDays = ticket.RequiredDays;
            }
        }
    }

    internal static double CheckDays(double days)
    {
        if (double.IsNaN(days) || days < 0)
        {
            throw new KanbanException("Work days cannot be negative.");
        }
        if (days > MaxDays)
        {
            throw new KanbanException($"Work days have to stay below {MaxDays}.");
        }
        return Math.Round(days, 2);
    }

    private static string CleanName(string? name, string what)
    {
        string clean = (name ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (clean.Length == 0)
        {
            throw new KanbanException($"The {what} needs a name.");
        }
        return clean.Length <= MaxTitle ? clean : clean[..MaxTitle].TrimEnd();
    }

    private async Task<KanbanProject> ProjectAsync(string userId, string projectId)
    {
        KanbanProject? project = string.IsNullOrEmpty(projectId) ? null : await _projects.GetDocumentByIdAsync(projectId);
        if (project == null || project.UserId != userId)
        {
            throw new KanbanException("That project does not exist.");
        }
        return project;
    }

    private async Task<(KanbanBoard Board, KanbanTicket Ticket)> TicketAsync(string userId, string ticketId)
    {
        KanbanTicket? found = string.IsNullOrEmpty(ticketId) ? null : await _tickets.GetDocumentByIdAsync(ticketId);
        if (found == null || found.UserId != userId)
        {
            throw new KanbanException("That ticket does not exist.");
        }
        KanbanBoard board = await LoadAsync(userId, found.ProjectId);
        // Work on the board's copy, so the board answers with the change
        return (board, board.Ticket(found.Id!) ?? found);
    }

    private async Task SaveAllAsync(KanbanBoard board)
    {
        foreach (KanbanTicket ticket in board.Tickets)
        {
            await _tickets.SaveDocumentAsync(ticket);
        }
    }

    private async Task TouchAsync(KanbanProject project)
    {
        DateTime now = _clock();
        // Strictly increasing, so two changes within one clock tick still look like a change
        project.UpdatedAt = now > project.UpdatedAt ? now : project.UpdatedAt.AddMilliseconds(1);
        await _projects.SaveDocumentAsync(project);
    }
}
