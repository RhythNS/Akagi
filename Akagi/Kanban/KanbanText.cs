using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Kanban;
using System.Globalization;
using System.Text;

namespace Akagi.Kanban;

/// <summary>
/// Plain text views of projects for models: the project list, a board, one ticket and the plan.
/// </summary>
internal static class KanbanText
{
    public static string Days(double days) => days.ToString("0.##", CultureInfo.InvariantCulture) + (Math.Abs(days - 1) < 1e-9 ? " day" : " days");

    public static string Date(DateOnly date) => date.ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Date(DateTime date) => Date(DateOnly.FromDateTime(date));

    public static string ProjectList(IReadOnlyList<KanbanProjectView> projects)
    {
        if (projects.Count == 0)
        {
            return "There are no projects yet.";
        }
        StringBuilder text = new();
        foreach (KanbanProjectView project in projects)
        {
            text.Append($"- {project.Name}: {project.OpenTickets} open of {project.TicketCount} tickets, {Days(project.RemainingDays)} of work left");
            if (project.Finish is DateTime finish)
            {
                text.Append($", plan finishes {Date(finish)}");
            }
            if (project.Archived)
            {
                text.Append(" (archived)");
            }
            text.AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    public static string Columns(KanbanProject project) =>
        string.Join(", ", project.Columns.Select(c => $"{c.Name} ({KanbanViews.KindName(c.Kind)})"));

    /// <summary>
    /// The board column by column. Done columns show only their latest tickets.
    /// </summary>
    public static string Board(KanbanBoard board, KanbanPlan plan, int doneShown = 5, bool details = true)
    {
        StringBuilder text = new();
        foreach (KanbanColumn column in board.Project.Columns)
        {
            List<KanbanTicket> tickets = [.. board.InColumn(column.Id)];
            text.AppendLine($"{column.Name} ({KanbanViews.KindName(column.Kind)}, {tickets.Count}):");
            IEnumerable<KanbanTicket> shown = column.Kind == KanbanColumnKind.Done
                ? tickets.OrderByDescending(t => t.DoneAt ?? t.UpdatedAt).Take(doneShown)
                : tickets;
            foreach (KanbanTicket ticket in shown)
            {
                text.AppendLine("- " + TicketLine(board, ticket, plan, details));
            }
            int hidden = tickets.Count - shown.Count();
            if (hidden > 0)
            {
                text.AppendLine($"- ... and {hidden} older");
            }
        }
        return text.ToString().TrimEnd();
    }

    public static string TicketLine(KanbanBoard board, KanbanTicket ticket, KanbanPlan plan, bool details = true)
    {
        StringBuilder line = new($"{ticket.Reference} {ticket.Title}");
        if (!details)
        {
            return line.ToString();
        }
        KanbanColumnKind kind = board.Project.KindOf(ticket.ColumnId);
        List<string> notes = [];
        if (kind == KanbanColumnKind.Done)
        {
            if (ticket.DoneAt is DateTime done)
            {
                notes.Add($"finished {Date(done)}");
            }
        }
        else
        {
            notes.Add(Math.Abs(ticket.RemainingDays - ticket.RequiredDays) < 1e-9
                ? Days(ticket.RequiredDays)
                : $"{Days(ticket.RemainingDays)} left of {Days(ticket.RequiredDays)}");
            List<string> waits = [.. ticket.After.Select(id => board.Ticket(id)).OfType<KanbanTicket>()
                .Where(t => board.Project.KindOf(t.ColumnId) != KanbanColumnKind.Done)
                .Select(t => t.Reference)];
            if (waits.Count > 0)
            {
                notes.Add("waits for " + string.Join(", ", waits));
            }
            if (plan.Find(ticket.Id!) is KanbanPlannedTicket planned)
            {
                notes.Add($"planned {Range(planned)}");
            }
        }
        return line.Append(" [").Append(string.Join("; ", notes)).Append(']').ToString();
    }

    public static string Ticket(KanbanBoard board, KanbanTicket ticket, KanbanPlan plan, int updatesShown = 20)
    {
        KanbanProject project = board.Project;
        KanbanColumn? column = project.Column(ticket.ColumnId);
        StringBuilder text = new();
        text.AppendLine($"{ticket.Reference} {ticket.Title}");
        text.AppendLine($"Project: {project.Name}; column: {column?.Name ?? "?"} ({KanbanViews.KindName(project.KindOf(ticket.ColumnId))})");
        text.AppendLine($"Required: {Days(ticket.RequiredDays)}; remaining: {Days(ticket.RemainingDays)}");
        List<string> before = [.. ticket.After.Select(id => board.Ticket(id)).OfType<KanbanTicket>().Select(t => Short(board, t))];
        if (before.Count > 0)
        {
            text.AppendLine("Waits for: " + string.Join(", ", before));
        }
        List<string> after = [.. board.Tickets.Where(t => t.After.Contains(ticket.Id!)).Select(t => Short(board, t))];
        if (after.Count > 0)
        {
            text.AppendLine("Needed by: " + string.Join(", ", after));
        }
        if (ticket.NotBefore is DateTime notBefore)
        {
            text.AppendLine($"Not before: {Date(notBefore)}");
        }
        if (plan.Find(ticket.Id!) is KanbanPlannedTicket planned && !planned.IsHistory)
        {
            text.AppendLine($"Planned: {Range(planned)}{(planned.Critical ? " (on the critical path)" : string.Empty)}");
        }
        if (ticket.StartedAt is DateTime started)
        {
            text.AppendLine($"Started: {Date(started)}");
        }
        if (ticket.DoneAt is DateTime done)
        {
            text.AppendLine($"Finished: {Date(done)}");
        }
        text.AppendLine();
        text.AppendLine("Description:");
        text.AppendLine(string.IsNullOrWhiteSpace(ticket.Description) ? "(none)" : ticket.Description.Trim());
        if (ticket.Updates.Count > 0)
        {
            text.AppendLine();
            text.AppendLine($"Updates ({ticket.Updates.Count}, oldest first):");
            foreach (KanbanUpdate update in ticket.Updates.TakeLast(updatesShown))
            {
                string days = update.RemainingDays is double remaining ? $", {Days(remaining)} left" : string.Empty;
                text.AppendLine($"[{update.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, {update.Author}{days}]");
                text.AppendLine(update.Text.Trim());
            }
        }
        return text.ToString().TrimEnd();
    }

    public static string Plan(KanbanBoard board, KanbanPlan plan)
    {
        KanbanProject project = board.Project;
        List<KanbanPlannedTicket> open = [.. plan.Items.Where(i => !i.IsHistory)];
        StringBuilder text = new();
        string workDays = string.Join(" ", Enum.GetValues<DayOfWeek>().Where(d => (project.WorkDays & (1 << (int)d)) != 0)
            .Select(d => CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(d)));
        text.AppendLine($"Plan for {project.Name} from {Date(plan.Calendar.Origin)}; work days {workDays}; {project.Parallel} ticket(s) at a time.");
        if (open.Count == 0)
        {
            text.Append("Nothing is open.");
            return text.ToString();
        }
        text.AppendLine($"{open.Count} open tickets, {Days(plan.RemainingDays)} of work; finishes {Date(plan.FinishDate!.Value)}.");
        foreach (KanbanPlannedTicket item in open)
        {
            List<string> notes = [Days(item.Ticket.RemainingDays), KanbanViews.KindName(item.Kind)];
            if (item.Critical)
            {
                notes.Add("critical");
            }
            if (item.InCycle)
            {
                notes.Add("in a dependency cycle");
            }
            if (item.WaitsForCapacity)
            {
                notes.Add("waits for a free slot");
            }
            if (item.Ticket.NotBefore is DateTime notBefore)
            {
                notes.Add($"not before {Date(notBefore)}");
            }
            text.AppendLine($"- {Range(item)}: {item.Ticket.Reference} {item.Ticket.Title} ({string.Join(", ", notes)})");
        }
        return text.ToString().TrimEnd();
    }

    private static string Range(KanbanPlannedTicket item) =>
        item.StartDate == item.EndDate ? Date(item.StartDate) : $"{Date(item.StartDate)} to {Date(item.EndDate)}";

    private static string Short(KanbanBoard board, KanbanTicket ticket)
    {
        string state = board.Project.KindOf(ticket.ColumnId) == KanbanColumnKind.Done ? " (done)" : string.Empty;
        return $"{ticket.Reference} {ticket.Title}{state}";
    }
}

/// <summary>
/// Finds projects, tickets and columns by what a model or person calls them.
/// </summary>
internal static class KanbanLookup
{
    public static KanbanProject Project(IReadOnlyList<KanbanProject> projects, string? name)
    {
        List<KanbanProject> active = [.. projects.Where(p => !p.Archived)];
        if (string.IsNullOrWhiteSpace(name))
        {
            if (active.Count == 1)
            {
                return active[0];
            }
            throw new KanbanException(projects.Count == 0
                ? "There are no projects yet."
                : $"Name the project. Projects: {string.Join(", ", projects.Select(p => p.Name))}.");
        }
        string wanted = name.Trim();
        KanbanProject? exact = projects.FirstOrDefault(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }
        List<KanbanProject> partial = [.. projects.Where(p => p.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                                                          || wanted.Contains(p.Name, StringComparison.OrdinalIgnoreCase))];
        if (partial.Count == 1)
        {
            return partial[0];
        }
        string available = projects.Count == 0 ? "none" : string.Join(", ", projects.Select(p => p.Name));
        throw new KanbanException(partial.Count == 0
            ? $"There is no project called '{wanted}'. Projects: {available}."
            : $"'{wanted}' matches several projects: {string.Join(", ", partial.Select(p => p.Name))}.");
    }

    /// <summary>
    /// A ticket by number ("#12" or "12") or by title.
    /// </summary>
    public static KanbanTicket Ticket(KanbanBoard board, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new KanbanException("Name the ticket by its number, e.g. #3.");
        }
        string wanted = reference.Trim();
        string number = wanted.TrimStart('#').Trim();
        int space = number.IndexOf(' ');
        if (int.TryParse(space > 0 ? number[..space] : number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
        {
            return board.Tickets.FirstOrDefault(t => t.Number == n)
                ?? throw new KanbanException($"{board.Project.Name} has no ticket #{n}.");
        }
        KanbanTicket? exact = board.Tickets.FirstOrDefault(t => string.Equals(t.Title, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }
        List<KanbanTicket> partial = [.. board.Tickets.Where(t => t.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase))];
        return partial.Count switch
        {
            1 => partial[0],
            0 => throw new KanbanException($"{board.Project.Name} has no ticket '{wanted}'."),
            _ => throw new KanbanException($"'{wanted}' matches several tickets: {string.Join(", ", partial.Take(8).Select(t => $"{t.Reference} {t.Title}"))}.")
        };
    }

    /// <summary>
    /// Every ticket reference in a list like "#1, #4 #7".
    /// </summary>
    public static List<KanbanTicket> Tickets(KanbanBoard board, string? references)
    {
        if (string.IsNullOrWhiteSpace(references))
        {
            return [];
        }
        string[] parts = references.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. parts.Select(p => Ticket(board, p)).Distinct()];
    }

    /// <summary>
    /// A column by name, or the first column of a kind ("to do", "in progress", "done").
    /// </summary>
    public static KanbanColumn Column(KanbanProject project, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new KanbanException($"Name the column. Columns: {KanbanText.Columns(project)}.");
        }
        string wanted = name.Trim();
        KanbanColumn? exact = project.Columns.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }
        List<KanbanColumn> partial = [.. project.Columns.Where(c => c.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))];
        if (partial.Count == 1)
        {
            return partial[0];
        }
        if (ParseKind(wanted) is KanbanColumnKind kind)
        {
            return project.FirstOf(kind);
        }
        throw new KanbanException($"{project.Name} has no column '{wanted}'. Columns: {KanbanText.Columns(project)}.");
    }

    public static KanbanColumnKind? ParseKind(string text)
    {
        string key = new([.. text.ToLowerInvariant().Where(char.IsLetter)]);
        return key switch
        {
            "todo" or "backlog" or "open" or "planned" => KanbanColumnKind.Todo,
            "inprogress" or "progress" or "doing" or "working" or "wip" or "active" => KanbanColumnKind.InProgress,
            "done" or "finished" or "closed" or "complete" or "completed" => KanbanColumnKind.Done,
            _ => null
        };
    }

    /// <summary>
    /// Columns written as "Backlog: todo, Working: in progress, Done: done".
    /// </summary>
    public static List<KanbanColumn> ParseColumns(string text)
    {
        List<KanbanColumn> columns = [];
        foreach (string part in text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.LastIndexOf(':');
            string name = colon > 0 ? part[..colon].Trim() : part;
            KanbanColumnKind? kind = colon > 0 ? ParseKind(part[(colon + 1)..]) : ParseKind(part);
            if (kind == null)
            {
                throw new KanbanException($"'{part}' needs a type: write it as 'Name: todo', 'Name: in progress' or 'Name: done'.");
            }
            columns.Add(new KanbanColumn { Name = name, Kind = kind.Value });
        }
        return columns;
    }
}
