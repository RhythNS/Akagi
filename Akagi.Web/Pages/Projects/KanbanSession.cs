using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Bridge.Kanban;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;

namespace Akagi.Web.Pages.Projects;

/// <summary>
/// One open project on the page: its tickets, today's plan and the requests that change them. Every answer from the bot
/// carries the whole project, which replaces what the page shows.
/// </summary>
public sealed class KanbanSession
{
    private readonly ISocketClientContainer _sockets;
    private KanbanPlan? _plan;

    public KanbanSession(ISocketClientContainer sockets, string projectId)
    {
        _sockets = sockets;
        ProjectId = projectId;
    }

    public string ProjectId { get; }
    public KanbanProjectView? Project { get; private set; }
    public List<KanbanTicketView> Tickets { get; private set; } = [];
    public DateOnly Today { get; private set; } = DateOnly.FromDateTime(DateTime.Now);
    public string? Error { get; set; }
    /// <summary>The last request found no bot to answer it.</summary>
    public bool Offline { get; private set; }
    public bool Busy { get; private set; }
    /// <summary>Grows with every change, for components that cache what they draw.</summary>
    public int Version { get; private set; }

    /// <summary>Raised after the project changed, on whatever thread the answer arrived.</summary>
    public event Action? Changed;

    public KanbanPlan Plan => _plan ??= KanbanScheduler.Plan(Project ?? new KanbanProjectView(), Tickets, Today);

    public KanbanTicketView? Ticket(string? id) => Tickets.FirstOrDefault(t => t.Id == id);

    public KanbanColumnKind KindOf(KanbanTicketView ticket) => Project?.KindOf(ticket.ColumnId) ?? KanbanColumnKind.Todo;

    /// <summary>Tickets of a column in board order.</summary>
    public IEnumerable<KanbanTicketView> InColumn(string columnId) =>
        Tickets.Where(t => t.ColumnId == columnId).OrderBy(t => t.Rank).ThenBy(t => t.Number);

    /// <summary>Open tickets in priority order: in progress first, then by rank, as the plan takes them.</summary>
    public IEnumerable<KanbanTicketView> OpenByPriority() => Tickets
        .Where(t => KindOf(t) != KanbanColumnKind.Done)
        .OrderBy(t => KindOf(t) == KanbanColumnKind.InProgress ? 0 : 1)
        .ThenBy(t => t.Rank)
        .ThenBy(t => t.Number);

    public Task<bool> LoadAsync() => SendAsync(new KanbanRequestTransmission { Action = KanbanAction.Load });

    /// <summary>
    /// Loads again when a character or another tab changed the project since.
    /// </summary>
    public async Task<bool> RefreshIfChangedAsync()
    {
        if (Busy || Project == null)
        {
            return false;
        }
        KanbanResponseTransmission? peek = await RequestAsync(new KanbanRequestTransmission { Action = KanbanAction.Peek, ProjectId = ProjectId }, quiet: true);
        if (peek?.Project == null || peek.Project.UpdatedAt <= Project.UpdatedAt)
        {
            return false;
        }
        return await LoadAsync();
    }

    /// <summary>
    /// Sends a change of this project and shows the project as it came back. False when it failed; see <see cref="Error"/>.
    /// </summary>
    public async Task<bool> SendAsync(KanbanRequestTransmission request)
    {
        if (string.IsNullOrEmpty(request.ProjectId))
        {
            request.ProjectId = ProjectId;
        }
        Busy = true;
        try
        {
            KanbanResponseTransmission? response = await RequestAsync(request, quiet: false);
            if (response == null)
            {
                return false;
            }
            Apply(response);
            return response.Error == null;
        }
        finally
        {
            Busy = false;
        }
    }

    public void Apply(KanbanResponseTransmission response)
    {
        Error = response.Error;
        if (response.Project != null && response.Project.Id == ProjectId && response.Error == null)
        {
            Project = response.Project;
            Tickets = [.. response.Tickets];
        }
        if (response.Today != default)
        {
            Today = DateOnly.FromDateTime(response.Today);
        }
        _plan = null;
        Version++;
        Changed?.Invoke();
    }

    private async Task<KanbanResponseTransmission?> RequestAsync(KanbanRequestTransmission request, bool quiet)
    {
        SocketClient? client = await _sockets.EnsureConnectedAsync();
        if (client is null || !_sockets.IsConnected)
        {
            Offline = true;
            return null;
        }
        Offline = false;
        try
        {
            return await new KanbanRequest(request).Get(client, KanbanRequest.Timeout);
        }
        catch (TimeoutException)
        {
            if (!quiet)
            {
                Error = "That took too long. Try again in a moment.";
            }
            return null;
        }
    }

    /// <summary>
    /// Sends any Kanban request outside a project, such as listing projects.
    /// </summary>
    public static async Task<(KanbanResponseTransmission? Response, bool Offline)> RequestAsync(ISocketClientContainer sockets, KanbanRequestTransmission request)
    {
        SocketClient? client = await sockets.EnsureConnectedAsync();
        if (client is null || !sockets.IsConnected)
        {
            return (null, true);
        }
        try
        {
            return (await new KanbanRequest(request).Get(client, KanbanRequest.Timeout), false);
        }
        catch (TimeoutException)
        {
            return (new KanbanResponseTransmission { Error = "That took too long. Try again in a moment." }, false);
        }
    }

    public static string Days(double days) => days.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static string KindName(KanbanColumnKind kind) => kind switch
    {
        KanbanColumnKind.Todo => "To do",
        KanbanColumnKind.InProgress => "In progress",
        _ => "Done"
    };

    public static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
