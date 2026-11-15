using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Kanban;
using Akagi.Kanban;
using Akagi.Life;
using System.Globalization;
using System.Text;

namespace Akagi.Receivers.Commands.Kanban;

/// <summary>
/// Base for the tools that read and change the user's Kanban projects. Problems a model can fix (unknown ticket,
/// a cycle, a missing column) come back as the tool's answer instead of an error.
/// </summary>
internal abstract class KanbanCommand : Command
{
    public override bool ContinueAfterExecution => true;

    public override async Task<Command[]> Execute(Context context)
    {
        string output;
        try
        {
            output = await Run(context, Service(context), context.User.Id ?? throw new InvalidOperationException("User has no id."));
        }
        catch (KanbanException ex)
        {
            output = "Not done: " + ex.Message;
        }
        context.Conversation.AddMessage(CreateCommandMessage(output));
        return [];
    }

    protected abstract Task<string> Run(Context context, KanbanService service, string userId);

    internal static KanbanService Service(Context context) => new(
        context.DatabaseFactory.GetDatabase<IKanbanProjectDatabase>(),
        context.DatabaseFactory.GetDatabase<IKanbanTicketDatabase>());

    /// <summary>Today in the user's time zone.</summary>
    internal static async Task<DateOnly> Today(Context context)
    {
        LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(context.User.Id!);
        return DateOnly.FromDateTime(profile.ToLocal(DateTime.UtcNow));
    }

    protected async Task<KanbanBoard> Board(KanbanService service, string userId)
    {
        KanbanProject project = KanbanLookup.Project(await service.ProjectsAsync(userId), GetArgument("Project"));
        return await service.LoadAsync(userId, project.Id!);
    }

    /// <summary>Parses "2", "1.5" or "1,5" work days; null when the argument is missing.</summary>
    protected double? Days(string name)
    {
        string? text = GetArgument(name);
        if (text == null)
        {
            return null;
        }
        string number = text.Replace(',', '.').Replace("days", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("day", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("d", string.Empty).Trim();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double days))
        {
            throw new KanbanException($"'{text}' is not a number of work days.");
        }
        return days;
    }

    /// <summary>
    /// A day as yyyy-MM-dd; "none" clears it. Null when the argument is missing.
    /// </summary>
    protected (bool Set, DateTime? Day) DayArgument(string name)
    {
        string? text = GetArgument(name);
        if (text == null)
        {
            return (false, null);
        }
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            return (true, null);
        }
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
        {
            throw new KanbanException($"'{text}' is not a day; write it as yyyy-MM-dd.");
        }
        return (true, KanbanViews.Day(day));
    }

    /// <summary>How alike two titles must be (<see cref="Grounding.WordSimilarity"/>) to count as the same ticket.</summary>
    internal const double SameTitleSimilarity = 0.75;

    /// <summary>
    /// The open ticket whose title says the same as <paramref name="title"/>, if any. A model that repeats itself
    /// rewords the title each time ("Cancel old insurance", "Cancel the insurance"), so an exact match is not enough.
    /// </summary>
    internal static KanbanTicket? SameOpenTicket(KanbanBoard board, string title) => board.Tickets
        .Where(t => board.Project.KindOf(t.ColumnId) != KanbanColumnKind.Done)
        .Select(t => (Ticket: t, Similarity: Grounding.WordSimilarity(t.Title, title)))
        .Where(m => m.Similarity >= SameTitleSimilarity)
        .OrderByDescending(m => m.Similarity)
        .ThenBy(m => m.Ticket.Number)
        .Select(m => m.Ticket)
        .FirstOrDefault();

    protected static Argument Text(string name, string description, bool required = false) => new()
    {
        Name = name,
        Description = description,
        IsRequired = required,
        ArgumentType = Argument.Type.String
    };

    protected static Argument Number(string name, string description) => new()
    {
        Name = name,
        Description = description,
        ArgumentType = Argument.Type.Float
    };

    protected static Argument Flag(string name, string description) => new()
    {
        Name = name,
        Description = description,
        ArgumentType = Argument.Type.Bool
    };

    protected static Argument ProjectArgument() =>
        Text("Project", "Project name; may be left out when there is only one project.");

    protected static Argument TicketArgument() =>
        Text("Ticket", "Ticket number like #12 (or its exact title).", required: true);
}

internal class ListProjectsCommand : KanbanCommand
{
    public override string Name => "ListProjects";

    public override string Description =>
        "List the user's Kanban projects with open tickets, remaining work days and the day each plan finishes.";

    public override Argument[] GetDefaultArguments() => [];

    protected override async Task<string> Run(Context context, KanbanService service, string userId) =>
        KanbanText.ProjectList(await service.SummariesAsync(userId, await Today(context)));
}

internal class ShowBoardCommand : KanbanCommand
{
    public override string Name => "ShowBoard";

    public override string Description =>
        "Show a project's Kanban board: every column with its tickets (number, title, days, what they wait for, planned days).";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        Number("DoneShown", "How many of the latest finished tickets to show per done column (default 5).")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        int done = Math.Clamp(GetIntArgument("DoneShown") ?? 5, 0, 100);
        string description = string.IsNullOrWhiteSpace(board.Project.Description) ? string.Empty
            : Environment.NewLine + "About: " + Shorten(board.Project.Description, 600);
        return $"Project {board.Project.Name}{description}{Environment.NewLine}{Environment.NewLine}" +
               KanbanText.Board(board, board.Plan(await Today(context)), done);
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text.Trim() : text[..length].Trim() + " ...";
}

internal class ShowTicketCommand : KanbanCommand
{
    public override string Name => "ShowTicket";

    public override string Description =>
        "Open one ticket: its description, all updates, days, what it waits for and what waits for it, and when it is planned.";

    public override Argument[] GetDefaultArguments() => [ProjectArgument(), TicketArgument()];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanTicket ticket = KanbanLookup.Ticket(board, GetArgument("Ticket"));
        return KanbanText.Ticket(board, ticket, board.Plan(await Today(context)));
    }
}

internal class ShowPlanCommand : KanbanCommand
{
    public override string Name => "ShowPlan";

    public override string Description =>
        "Show a project's plan like a Gantt chart: when each open ticket starts and ends, the critical path and the finish day. " +
        "The plan follows priority, dependencies, work days and how many tickets are worked on at once.";

    public override Argument[] GetDefaultArguments() => [ProjectArgument()];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        return KanbanText.Plan(board, board.Plan(await Today(context)));
    }
}

internal class CreateProjectCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "CreateProject";

    public override string Description =>
        "Create a Kanban project. Columns default to Backlog (to do), In progress and Done.";

    public override Argument[] GetDefaultArguments() =>
    [
        Text("Name", "Project name.", required: true),
        Text("Description", "Markdown: what the project is and what to know about it."),
        Text("Columns", "Columns from left to right as 'Name: type', type being todo, in progress or done, e.g. " +
                        "'Backlog: todo, Working: in progress, Review: in progress, Done: done, Aborted: done'. Needs at least one of each type.")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        string? columnText = GetArgument("Columns");
        List<KanbanColumn>? columns = columnText == null ? null : KanbanLookup.ParseColumns(columnText);
        KanbanProject project = await service.CreateProjectAsync(userId, RequireArgument("Name"), GetArgument("Description") ?? string.Empty, columns);
        return $"Created project {project.Name} with columns {KanbanText.Columns(project)}.";
    }
}

internal class EditProjectCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "EditProject";

    public override string Description =>
        "Change a project: rename it, replace or extend its description (the knowledge everyone working on it gets), its plan " +
        "start day or how many tickets are worked on at once.";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        Text("NewName", "New project name."),
        Text("Description", "Markdown that replaces the whole description."),
        Text("AppendToDescription", "Markdown added at the end of the description, e.g. a decision or fact worth keeping."),
        Text("StartDate", "First day of the plan as yyyy-MM-dd, or 'none' to plan from today."),
        Number("Parallel", "How many tickets are worked on at the same time (default 1).")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanProjectView view = board.ProjectView();
        List<string> changes = [];
        if (GetArgument("NewName") is string name)
        {
            view.Name = name;
            changes.Add("renamed");
        }
        if (GetArgument("Description") is string description)
        {
            view.Description = description;
            changes.Add("description replaced");
        }
        if (GetArgument("AppendToDescription") is string append)
        {
            view.Description = string.IsNullOrWhiteSpace(view.Description) ? append : view.Description.TrimEnd() + Environment.NewLine + Environment.NewLine + append;
            changes.Add("description extended");
        }
        (bool setStart, DateTime? start) = DayArgument("StartDate");
        if (setStart)
        {
            view.StartDate = start;
            changes.Add(start is DateTime day ? $"plan starts {KanbanText.Date(day)}" : "plan starts today");
        }
        if (GetIntArgument("Parallel") is int parallel)
        {
            view.Parallel = parallel;
            changes.Add($"{Math.Clamp(parallel, 1, 50)} at a time");
        }
        if (changes.Count == 0)
        {
            return "Not done: nothing to change was given.";
        }
        KanbanBoard saved = await service.SaveProjectAsync(userId, view);
        return $"{saved.Project.Name}: {string.Join(", ", changes)}.";
    }
}

internal class CreateTicketCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "CreateTicket";

    public override string Description =>
        "Create a ticket on a project's board. It goes to the bottom of the column (the first to-do column by default).";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        Text("Title", "Short title; it is all the board shows.", required: true),
        Text("Description", "Markdown: what needs to be done, acceptance criteria, notes."),
        Number("RequiredDays", "Estimated work days, e.g. 0.5 or 3."),
        Text("Column", "Column name; default is the first to-do column."),
        Text("After", "Tickets that must be finished first, e.g. '#2, #5'."),
        Text("Before", "Tickets that must wait for this one, e.g. '#7'."),
        Text("NotBefore", "Do not plan it before this day (yyyy-MM-dd).")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        string title = RequireArgument("Title");
        if (SameOpenTicket(board, title) is KanbanTicket same)
        {
            return $"Not done: {same.Reference} {same.Title} is already open and says the same. Change that ticket with " +
                   "UpdateTicket or AddTicketUpdate, or give this one a title that tells them apart.";
        }
        KanbanColumn? column = GetArgument("Column") is string columnName ? KanbanLookup.Column(board.Project, columnName) : null;
        List<KanbanTicket> after = KanbanLookup.Tickets(board, GetArgument("After"));
        List<KanbanTicket> before = KanbanLookup.Tickets(board, GetArgument("Before"));
        (_, DateTime? notBefore) = DayArgument("NotBefore");

        KanbanTicket ticket = await service.CreateTicketAsync(userId, board.Project.Id!, title, GetArgument("Description"),
            column?.Id, Days("RequiredDays") ?? 0, null, after.Select(t => t.Id!), notBefore);
        List<string> notes = [];
        foreach (KanbanTicket later in before)
        {
            try
            {
                await service.LinkAsync(userId, ticket.Id!, later.Id!);
                notes.Add($"{later.Reference} now waits for it");
            }
            catch (KanbanException ex)
            {
                // The ticket exists either way; say what did not work instead of failing the whole call
                notes.Add($"{later.Reference} could not be linked: {ex.Message}");
            }
        }
        if (after.Count > 0)
        {
            notes.Add("waits for " + string.Join(", ", after.Select(t => t.Reference)));
        }
        string columnShown = board.Project.Column(ticket.ColumnId)?.Name ?? "?";
        return $"Created {ticket.Reference} {ticket.Title} in {columnShown} ({KanbanText.Days(ticket.RequiredDays)})" +
               (notes.Count > 0 ? "; " + string.Join("; ", notes) : string.Empty) + ".";
    }
}

internal class CreateTicketsCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "CreateTickets";

    public override string Description =>
        "Create several tickets at once, e.g. when planning a project. One ticket per line: 'Title | work days | description'. " +
        "Tickets already open on the board are not created twice. Link them afterwards with LinkTickets.";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        Text("Tickets", "One per line: 'Title | work days | description' (days and description optional). Lines without '|' " +
                        "after a description continue it, so it may be Markdown over several lines.", required: true),
        Text("Column", "Column for all of them; default is the first to-do column.")
    ];

    /// <summary>Each ticket counts against the board change budget, so repeating a whole plan runs out of it.</summary>
    public int Changes
    {
        get
        {
            try
            {
                return Math.Max(1, Parse(GetArgument("Tickets") ?? string.Empty).Count);
            }
            catch (KanbanException)
            {
                // The call fails when it runs and says why
                return 1;
            }
        }
    }

    internal sealed record TicketLine(string Title, double Days, string? Description);

    /// <summary>
    /// Reads 'Title | days | description' lines. Once any line uses '|', a line without one that follows a description
    /// belongs to that description (a Markdown list, a "Done when ..." line); otherwise every line is a title.
    /// </summary>
    internal static List<TicketLine> Parse(string text)
    {
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        bool structured = lines.Any(l => l.Contains('|'));
        List<(string Title, double Days, StringBuilder? Description)> tickets = [];
        StringBuilder? openDescription = null;

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (structured && openDescription != null && !trimmed.Contains('|'))
            {
                openDescription.Append('\n').Append(line.TrimEnd());
                continue;
            }
            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] parts = trimmed.TrimStart('-', '*', ' ').Split('|', 3, StringSplitOptions.TrimEntries);
            double days = 0;
            if (parts.Length > 1 && parts[1].Length > 0 &&
                !double.TryParse(parts[1].Replace(',', '.').Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out days))
            {
                throw new KanbanException($"'{parts[1]}' in '{trimmed}' is not a number of work days.");
            }
            openDescription = parts.Length > 2 ? new StringBuilder(parts[2]) : null;
            tickets.Add((parts[0], KanbanService.CheckDays(days), openDescription));
        }

        return [.. tickets.Select(t => new TicketLine(t.Title, t.Days,
            t.Description?.ToString().Trim() is { Length: > 0 } description ? description : null))];
    }

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanColumn? column = GetArgument("Column") is string columnName ? KanbanLookup.Column(board.Project, columnName) : null;

        // Check every line before creating anything, so a typo does not leave half a plan
        List<TicketLine> parsed = Parse(RequireArgument("Tickets"));
        if (parsed.Count > ToolGuard.MaxWorkItemChangesPerStep)
        {
            return $"Not done: at most {ToolGuard.MaxWorkItemChangesPerStep} tickets at once; create the rest in the next reply.";
        }

        List<TicketLine> fresh = [];
        List<string> skipped = [];
        foreach (TicketLine line in parsed)
        {
            if (SameOpenTicket(board, line.Title) is KanbanTicket open)
            {
                skipped.Add($"- {line.Title}: {open.Reference} {open.Title} is already open");
            }
            else if (fresh.FirstOrDefault(f => Grounding.WordSimilarity(f.Title, line.Title) >= SameTitleSimilarity) is TicketLine earlier)
            {
                skipped.Add($"- {line.Title}: same as {earlier.Title} in this list");
            }
            else
            {
                fresh.Add(line);
            }
        }
        string skippedText = skipped.Count == 0 ? string.Empty
            : "Not created, already there (change those tickets instead, or give a title that tells them apart):" +
              Environment.NewLine + string.Join(Environment.NewLine, skipped);
        if (fresh.Count == 0)
        {
            return "Not done: every ticket is on the board already." + Environment.NewLine + skippedText;
        }

        StringBuilder created = new("Created:");
        foreach (TicketLine line in fresh)
        {
            KanbanTicket ticket = await service.CreateTicketAsync(userId, board.Project.Id!, line.Title, line.Description, column?.Id, line.Days);
            created.Append(Environment.NewLine).Append($"- {ticket.Reference} {ticket.Title} ({KanbanText.Days(line.Days)})");
        }
        if (skipped.Count > 0)
        {
            created.Append(Environment.NewLine).Append(skippedText);
        }
        return created.ToString();
    }
}

internal class UpdateTicketCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "UpdateTicket";

    public override string Description =>
        "Change a ticket's title, description, required or remaining work days, or the day it may start. To report progress " +
        "use AddTicketUpdate; to change its column use MoveTicket.";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        TicketArgument(),
        Text("Title", "New title."),
        Text("Description", "Markdown that replaces the description."),
        Number("RequiredDays", "New total estimate in work days. Before work starts the remaining days follow it."),
        Number("RemainingDays", "Work days still left."),
        Text("NotBefore", "Do not plan it before this day (yyyy-MM-dd), or 'none'.")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanTicket ticket = KanbanLookup.Ticket(board, GetArgument("Ticket"));
        (bool setNotBefore, DateTime? notBefore) = DayArgument("NotBefore");
        KanbanTicketChange change = new()
        {
            Title = GetArgument("Title"),
            Description = GetArgument("Description"),
            RequiredDays = Days("RequiredDays"),
            RemainingDays = Days("RemainingDays"),
            SetNotBefore = setNotBefore,
            NotBefore = notBefore
        };
        if (change is { Title: null, Description: null, RequiredDays: null, RemainingDays: null, SetNotBefore: false })
        {
            return "Not done: nothing to change was given.";
        }
        (_, KanbanTicket changed) = await service.UpdateTicketAsync(userId, ticket.Id!, change);
        return $"Updated {changed.Reference} {changed.Title}: {KanbanText.Days(changed.RemainingDays)} left of {KanbanText.Days(changed.RequiredDays)}.";
    }
}

internal class MoveTicketCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "MoveTicket";

    public override string Description =>
        "Move a ticket to another column, e.g. to an in-progress column when work starts or to Done (or Aborted) when it ends.";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        TicketArgument(),
        Text("Column", "Column name, or a type: todo, in progress, done.", required: true),
        Flag("Top", "Put it at the top of the column (most important) instead of the bottom.")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanTicket ticket = KanbanLookup.Ticket(board, GetArgument("Ticket"));
        KanbanColumn column = KanbanLookup.Column(board.Project, GetArgument("Column"));
        List<string>? order = null;
        if (GetBoolArgument("Top") == true)
        {
            order = [ticket.Id!, .. board.InColumn(column.Id).Where(t => t.Id != ticket.Id).Select(t => t.Id!)];
        }
        string from = board.Project.Column(ticket.ColumnId)?.Name ?? "?";
        (KanbanBoard after, KanbanTicket moved) = await service.MoveTicketAsync(userId, ticket.Id!, column.Id, order);
        string unblocked = string.Empty;
        if (column.Kind == KanbanColumnKind.Done)
        {
            List<string> free = [.. after.Tickets
                .Where(t => t.After.Contains(moved.Id!) && after.Project.KindOf(t.ColumnId) != KanbanColumnKind.Done)
                .Where(t => t.After.All(id => after.Ticket(id) is not KanbanTicket b || after.Project.KindOf(b.ColumnId) == KanbanColumnKind.Done))
                .Select(t => $"{t.Reference} {t.Title}")];
            if (free.Count > 0)
            {
                unblocked = " Ready to start now: " + string.Join(", ", free) + ".";
            }
        }
        return $"Moved {moved.Reference} {moved.Title} from {from} to {column.Name}.{unblocked}";
    }
}

internal class AddTicketUpdateCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "AddTicketUpdate";

    public override string Description =>
        "Add a progress update to a ticket (what was done, problems, decisions), optionally with the work days still left.";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        TicketArgument(),
        Text("Text", "Markdown update.", required: true),
        Number("RemainingDays", "Work days still left after this update.")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanTicket ticket = KanbanLookup.Ticket(board, GetArgument("Ticket"));
        (_, KanbanTicket changed) = await service.AddUpdateAsync(userId, ticket.Id!, context.Character.Name, RequireArgument("Text"), Days("RemainingDays"));
        return $"Added update {changed.Updates.Count} to {changed.Reference} {changed.Title} ({KanbanText.Days(changed.RemainingDays)} left).";
    }
}

internal class LinkTicketsCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "LinkTickets";

    public override string Description =>
        "Set which tickets have to be finished before others. 'A > B' means B can only start once A is finished.";

    public override Argument[] GetDefaultArguments() =>
    [
        ProjectArgument(),
        Text("Links", "Pairs separated by commas, e.g. '#1 > #2, #2 > #5'.", required: true),
        Flag("Remove", "Remove these links instead of adding them.")
    ];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        bool remove = GetBoolArgument("Remove") == true;
        List<(KanbanTicket First, KanbanTicket Later)> pairs = [];
        foreach (string pair in RequireArgument("Links").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] sides = pair.Split(['>'], 2, StringSplitOptions.TrimEntries);
            if (sides.Length != 2)
            {
                return $"Not done: write '{pair}' as 'first > later'.";
            }
            pairs.Add((KanbanLookup.Ticket(board, sides[0].TrimEnd('-', '=')), KanbanLookup.Ticket(board, sides[1])));
        }

        List<string> done = [];
        List<string> refused = [];
        foreach ((KanbanTicket first, KanbanTicket later) in pairs)
        {
            try
            {
                if (remove)
                {
                    await service.UnlinkAsync(userId, first.Id!, later.Id!);
                }
                else
                {
                    await service.LinkAsync(userId, first.Id!, later.Id!);
                }
                done.Add($"{first.Reference} > {later.Reference}");
            }
            catch (KanbanException ex)
            {
                refused.Add(ex.Message);
            }
        }
        string result = done.Count == 0 ? string.Empty : (remove ? "Removed " : "Linked ") + string.Join(", ", done) + ".";
        if (refused.Count > 0)
        {
            result = (result + " Not done: " + string.Join(" ", refused)).Trim();
        }
        return result;
    }
}

internal class DeleteTicketCommand : KanbanCommand, IWorkItemCommand
{
    public override string Name => "DeleteTicket";

    public override string Description =>
        "Delete a ticket that was created by mistake or twice. Work that was dropped belongs in a done column such as Aborted instead.";

    public override Argument[] GetDefaultArguments() => [ProjectArgument(), TicketArgument()];

    protected override async Task<string> Run(Context context, KanbanService service, string userId)
    {
        KanbanBoard board = await Board(service, userId);
        KanbanTicket ticket = KanbanLookup.Ticket(board, GetArgument("Ticket"));
        await service.DeleteTicketAsync(userId, ticket.Id!);
        return $"Deleted {ticket.Reference} {ticket.Title}.";
    }
}
