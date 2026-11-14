using AngleSharp.Dom;
using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Web.Models;
using Akagi.Web.Pages.Projects;
using Akagi.Web.Pages.Projects.Components;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Sockets.Requests;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace Akagi.Web.Tests;

/// <summary>
/// The project pages against a client whose requests the test answers itself: the board, a ticket, the plan and the
/// settings.
/// </summary>
public class KanbanPageTests : BunitContext
{
    private sealed class Sockets : ISocketClientContainer
    {
        public SocketClient? Client { get; init; }
        public bool IsConnected => Client != null;
#pragma warning disable CS0067
        public event Action? ConnectionStateChanged;
#pragma warning restore CS0067
        public Task<SocketClient?> EnsureConnectedAsync() => Task.FromResult(Client);
        public Task TryConnectAsync() => Task.CompletedTask;
        public Task TryDisconnectAsync() => Task.CompletedTask;
    }

    private readonly SocketClient _client;
    private readonly HashSet<IRequest> _answered = [];

    // A Monday
    private static readonly DateTime Today = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

    public KanbanPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        ProjectPage.RefreshInterval = TimeSpan.FromHours(1);
        _client = new SocketClient(IPAddress.Loopback, 1, new User { GoogleId = "g", Name = "Tester", Email = "t" }, "circuit",
            NullLoggerFactory.Instance, [], null!);
        GC.SuppressFinalize(_client);
        Services.AddSingleton<ISocketClientContainer>(new Sockets { Client = _client });
    }

    private KanbanRequestTransmission Respond(Func<KanbanRequestTransmission, KanbanResponseTransmission> answer)
    {
        KanbanRequest? request = null;
        for (int i = 0; i < 300 && request == null; i++)
        {
            request = _client.GetRequests<KanbanRequest>().FirstOrDefault(r => !_answered.Contains(r));
            if (request == null)
            {
                Thread.Sleep(10);
            }
        }
        Assert.NotNull(request);
        _answered.Add(request);
        KanbanResponseTransmission response = answer(request.Transmission);
        response.RequestId = request.RequestId;
        if (response.Today == default)
        {
            response.Today = Today;
        }
        request.Fulfill(response);
        return request.Transmission;
    }

    private static KanbanProjectView Project() => new()
    {
        Id = "p1",
        Name = "Akagi",
        Description = "Personal **companion**.",
        Parallel = 1,
        UpdatedAt = Today,
        Columns =
        [
            new KanbanColumnView { Id = "todo", Name = "Backlog", Kind = KanbanColumnKind.Todo },
            new KanbanColumnView { Id = "doing", Name = "Working", Kind = KanbanColumnKind.InProgress },
            new KanbanColumnView { Id = "review", Name = "Review", Kind = KanbanColumnKind.InProgress },
            new KanbanColumnView { Id = "done", Name = "Done", Kind = KanbanColumnKind.Done }
        ]
    };

    private static KanbanTicketView[] Tickets() =>
    [
        new() { Id = "a", Number = 1, Title = "Data model", ColumnId = "doing", Rank = 0, RequiredDays = 2, RemainingDays = 1, Description = "Write **records**.",
                Updates = [new KanbanUpdateView { Id = "u1", Author = "Ren", At = Today, Text = "Started with `KanbanProject`.", RemainingDays = 1 }] },
        new() { Id = "b", Number = 2, Title = "Board page", ColumnId = "todo", Rank = 1, RequiredDays = 2, RemainingDays = 2, After = ["a"] },
        new() { Id = "c", Number = 3, Title = "Gantt page", ColumnId = "todo", Rank = 2, RequiredDays = 3, RemainingDays = 3, After = ["a"] },
        new() { Id = "d", Number = 4, Title = "Setup", ColumnId = "done", Rank = 3, RequiredDays = 1, DoneAt = Today.AddDays(-3), StartedAt = Today.AddDays(-4) }
    ];

    private static KanbanResponseTransmission Loaded(KanbanTicketView[]? tickets = null, KanbanProjectView? project = null) =>
        new() { Project = project ?? Project(), Tickets = tickets ?? Tickets() };

    private IRenderedComponent<ProjectPage> Open(string? view = null)
    {
        IRenderedComponent<ProjectPage> cut = Render<ProjectPage>(p => p.Add(x => x.ProjectId, "p1").Add(x => x.View, view));
        KanbanRequestTransmission load = Respond(_ => Loaded());
        Assert.Equal(KanbanAction.Load, load.Action);
        Assert.Equal("p1", load.ProjectId);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-project-title]")));
        return cut.Settled();
    }

    [Fact]
    public void Board_ShowsColumnsWithOnlyTicketTitles()
    {
        IRenderedComponent<ProjectPage> cut = Open();

        Assert.Equal(["Backlog", "Working", "Review", "Done"], cut.FindAll("[data-column-name]").Select(e => e.TextContent));
        Assert.Equal(["Board page", "Gantt page"], cut.FindAll("[data-column=todo] [data-ticket]").Select(e => e.TextContent.Trim()));
        Assert.Equal("Data model", cut.Find("[data-ticket=a]").TextContent.Trim());
        Assert.Equal("5 work days", cut.Find("[data-column=todo] [data-column-days]").TextContent.Trim());
        Assert.Contains("3 open of 4 tickets", cut.Find("[data-project-stats]").TextContent);
        // One person: 1 day of #1, then 2 + 3 days: Monday to the next Monday
        Assert.Contains("finishes Mon 2026-11-23", cut.Find("[data-project-stats]").TextContent);
    }

    [Fact]
    public async Task Board_DropsATicketBeforeAnother()
    {
        IRenderedComponent<ProjectPage> cut = Open();

        await cut.Find("[data-ticket=c]").DragStartAsync(new DragEventArgs());
        await cut.Find("[data-ticket=a]").DragEnterAsync(new DragEventArgs());
        Assert.Single(cut.FindAll("[data-column=doing] .kanban-drop-marker"));
        Task drop = cut.Find("[data-column=doing]").DropAsync(new DragEventArgs());
        KanbanRequestTransmission move = Respond(_ => Loaded());
        await drop;

        Assert.Equal(KanbanAction.MoveTicket, move.Action);
        Assert.Equal("c", move.TicketId);
        Assert.Equal("doing", move.ColumnId);
        Assert.Equal(["c", "a"], move.OrderedIds);
    }

    [Fact]
    public void DropOrder_PutsTheTicketBeforeTheTargetOrLast()
    {
        Assert.Equal(["x", "a", "b"], KanbanBoardView.DropOrder(["a", "b"], "x", "a"));
        Assert.Equal(["a", "b", "x"], KanbanBoardView.DropOrder(["a", "b"], "x", null));
        Assert.Equal(["b", "a"], KanbanBoardView.DropOrder(["a", "b"], "a", null));
    }

    [Fact]
    public async Task Board_AddsATicketToAColumn()
    {
        IRenderedComponent<ProjectPage> cut = Open();

        await cut.Find("[data-add=review]").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-new-title]").InputAsync(new ChangeEventArgs { Value = "Write tests" });
        Task add = cut.Find("[data-add-ticket]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission save = Respond(_ => Loaded());
        await add;

        Assert.Equal(KanbanAction.SaveTicket, save.Action);
        Assert.Equal("Write tests", save.Ticket!.Title);
        Assert.Equal("review", save.Ticket.ColumnId);
        Assert.Equal(string.Empty, save.Ticket.Id);
    }

    [Fact]
    public async Task Ticket_ShowsMarkdownUpdatesAndLinks()
    {
        IRenderedComponent<ProjectPage> cut = Open();

        await cut.Find("[data-ticket=a]").ClickAsync(new MouseEventArgs());

        IElement dialog = cut.Find("[data-ticket-dialog=a]");
        Assert.Equal("#1", cut.Find("[data-ticket-number]").TextContent);
        Assert.Contains("<strong>records</strong>", dialog.QuerySelector("[data-markdown]")!.InnerHtml);
        Assert.Contains("<code>KanbanProject</code>", cut.Find("[data-update=u1]").InnerHtml);
        Assert.Equal("Ren", cut.Find("[data-update-author]").TextContent);
        Assert.Equal(["b", "c"], cut.FindAll("[data-links=after] [data-link]").Select(e => e.GetAttribute("data-link")));
        Assert.Empty(cut.FindAll("[data-links=before] [data-link]"));
        Assert.Contains("Mon 2026-11-16", cut.Find("[data-ticket-planned]").TextContent);
    }

    [Fact]
    public async Task Ticket_AddsAnUpdateWithRemainingDays()
    {
        IRenderedComponent<ProjectPage> cut = Open();
        await cut.Find("[data-ticket=a]").ClickAsync(new MouseEventArgs());

        await cut.Find("[data-new-update] [data-md-input]").InputAsync(new ChangeEventArgs { Value = "Records **done**" });
        await cut.Find("[data-update-remaining]").ChangeAsync(new ChangeEventArgs { Value = "0.5" });
        Task add = cut.Find("[data-add-update]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission update = Respond(_ => Loaded());
        await add;

        Assert.Equal(KanbanAction.AddUpdate, update.Action);
        Assert.Equal("a", update.TicketId);
        Assert.Equal("Records **done**", update.Text);
        Assert.Equal(0.5, update.RemainingDays);
    }

    [Fact]
    public async Task Ticket_EditsTheDescriptionWithAPreview()
    {
        IRenderedComponent<ProjectPage> cut = Open();
        await cut.Find("[data-ticket=b]").ClickAsync(new MouseEventArgs());

        await cut.Find("[data-edit-description]").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-md-editor=description] [data-md-input]").InputAsync(new ChangeEventArgs { Value = "# Goal\n- [ ] columns" });
        await cut.Find("[data-md-editor=description] [data-md-preview]").ClickAsync(new MouseEventArgs());
        Assert.Contains("<h1", cut.Find("[data-md-editor=description] [data-markdown]").InnerHtml);

        Task save = cut.Find("[data-save-description]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission request = Respond(_ => Loaded());
        await save;

        Assert.Equal(KanbanAction.SaveTicket, request.Action);
        Assert.Equal("b", request.Ticket!.Id);
        Assert.Equal("# Goal\n- [ ] columns", request.Ticket.Description);
        Assert.Equal("Board page", request.Ticket.Title);
        Assert.Equal("todo", request.Ticket.ColumnId);
    }

    [Fact]
    public async Task Ticket_LinksAnotherTicketItWaitsFor()
    {
        IRenderedComponent<ProjectPage> cut = Open();
        await cut.Find("[data-ticket=c]").ClickAsync(new MouseEventArgs());

        Task link = cut.Find("[data-links=before] [data-add-link]").ChangeAsync(new ChangeEventArgs { Value = "b" });
        KanbanRequestTransmission request = Respond(_ => Loaded());
        await link;

        Assert.Equal(KanbanAction.Link, request.Action);
        Assert.Equal("b", request.TicketId);
        Assert.Equal("c", request.OtherTicketId);
    }

    [Fact]
    public void Plan_DrawsBarsInPriorityOrderWithArrows()
    {
        IRenderedComponent<ProjectPage> cut = Open("plan");

        Assert.Equal(["a", "b", "c"], cut.FindAll("[data-row]").Select(e => e.GetAttribute("data-row")));
        Assert.Contains("Mon 2026-11-23", cut.Find("[data-plan-finish]").TextContent);
        // Weeks zoom: 22px per day, the timeline starts a day before today
        Assert.Contains("left:22px", cut.Find("[data-bar=a]").GetAttribute("style"));
        Assert.Contains("width:22px", cut.Find("[data-bar=a]").GetAttribute("style"));
        Assert.Contains("left:44px", cut.Find("[data-bar=b]").GetAttribute("style"));
        // #3 starts Thursday and ends Monday, so it spans the weekend
        Assert.Contains("width:110px", cut.Find("[data-bar=c]").GetAttribute("style"));
        Assert.Equal(2, cut.FindAll("[data-link]").Count);
        Assert.Contains("critical", cut.Find("[data-bar=c]").ClassName);
        Assert.Empty(cut.FindAll("[data-bar=d]"));
    }

    [Fact]
    public async Task Plan_ShowsFinishedWorkOnRequest()
    {
        IRenderedComponent<ProjectPage> cut = Open("plan");

        await cut.Find("[data-show-done]").ChangeAsync(new ChangeEventArgs { Value = true });

        Assert.Equal("d", cut.FindAll("[data-row]")[0].GetAttribute("data-row"));
        Assert.Contains("history", cut.Find("[data-bar=d]").ClassName);
    }

    [Fact]
    public async Task Plan_StepsDaysAndKeepsRequiredInStepBeforeWorkStarts()
    {
        IRenderedComponent<ProjectPage> cut = Open("plan");

        Task more = cut.Find("[data-row=b] [data-more]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission todo = Respond(_ => Loaded());
        await more;
        KanbanEstimateView estimate = Assert.Single(todo.Estimates);
        Assert.Equal(KanbanAction.Plan, todo.Action);
        Assert.Equal("b", estimate.TicketId);
        Assert.Equal(2.5, estimate.RequiredDays);
        Assert.Equal(2.5, estimate.RemainingDays);

        Task less = cut.Find("[data-row=a] [data-less]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission working = Respond(_ => Loaded());
        await less;
        estimate = Assert.Single(working.Estimates);
        Assert.Equal("a", estimate.TicketId);
        Assert.Null(estimate.RequiredDays);
        Assert.Equal(0.5, estimate.RemainingDays);
    }

    [Fact]
    public async Task Plan_DraggingABarChangesDaysOrStart()
    {
        IRenderedComponent<ProjectPage> cut = Open("plan");
        GanttView gantt = cut.FindComponent<GanttView>().Instance;

        // #3 starts Thursday (calendar 3); its end dragged to the middle of Tuesday (calendar 8.5) is 3.5 work days
        Task resize = cut.InvokeAsync(() => gantt.BarDragged("c", "resize", 3, 8.5));
        KanbanRequestTransmission request = Respond(_ => Loaded());
        await resize;
        KanbanEstimateView estimate = Assert.Single(request.Estimates);
        Assert.Equal(3.5, estimate.RequiredDays);
        Assert.Equal(3.5, estimate.RemainingDays);

        // Moved onto Saturday: it may start on Monday
        Task move = cut.InvokeAsync(() => gantt.BarDragged("b", "move", 5, 7));
        request = Respond(_ => Loaded());
        await move;
        estimate = Assert.Single(request.Estimates);
        Assert.True(estimate.SetNotBefore);
        Assert.Equal(new DateTime(2026, 11, 23), estimate.NotBefore);

        // Back to the start of the plan: no day needed
        Task back = cut.InvokeAsync(() => gantt.BarDragged("b", "move", -0.2, 1.8));
        request = Respond(_ => Loaded());
        await back;
        Assert.Null(Assert.Single(request.Estimates).NotBefore);
    }

    [Fact]
    public async Task Plan_ReordersWithinTheSameKindOnly()
    {
        IRenderedComponent<ProjectPage> cut = Open("plan");

        Assert.True(cut.Find("[data-row=b] [data-up]").HasAttribute("disabled"));
        Task up = cut.Find("[data-row=c] [data-up]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission request = Respond(_ => Loaded());
        await up;

        Assert.Equal(KanbanAction.Reorder, request.Action);
        Assert.Equal(["c", "b"], request.OrderedIds);
    }

    [Fact]
    public async Task Settings_NeedEveryKindOfColumnBeforeSaving()
    {
        IRenderedComponent<ProjectPage> cut = Open("settings");

        // Remove both in-progress columns
        await cut.FindAll("[data-remove-column]")[1].ClickAsync(new MouseEventArgs());
        await cut.FindAll("[data-remove-column]")[1].ClickAsync(new MouseEventArgs());

        Assert.Contains("Add at least one in progress column.", cut.FindAll("[data-problem]").Select(e => e.TextContent));
        Assert.True(cut.Find("[data-save-settings]").HasAttribute("disabled"));
        Assert.Contains("1 ticket(s) in Working will move", cut.Find("[data-removed-note]").TextContent);

        await cut.Find("[data-add-column]").ClickAsync(new MouseEventArgs());
        Assert.Empty(cut.FindAll("[data-problem]"));
        Task save = cut.Find("[data-save-settings]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission request = Respond(_ => Loaded());
        await save;

        Assert.Equal(KanbanAction.SaveProject, request.Action);
        Assert.Equal(["Backlog", "Done", "New column"], request.Project!.Columns.Select(c => c.Name));
        Assert.Equal(KanbanColumnKind.InProgress, request.Project.Columns[2].Kind);
    }

    [Fact]
    public async Task List_ShowsProjectsAndCreatesOne()
    {
        IRenderedComponent<ProjectListPage> cut = Render<ProjectListPage>();
        KanbanProjectView summary = Project();
        summary.OpenTickets = 3;
        summary.TicketCount = 4;
        summary.RemainingDays = 6;
        summary.Finish = new DateTime(2026, 11, 23);
        Respond(_ => new KanbanResponseTransmission { Projects = [summary] });
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-project]")));

        Assert.Equal("3", cut.Find("[data-open-count]").TextContent);
        Assert.Contains("Mon 2026-11-23", cut.Find("[data-finish]").TextContent);

        await cut.Find("[data-new]").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-new-name]").InputAsync(new ChangeEventArgs { Value = "Website" });
        await cut.Find("[data-new-preset]").ChangeAsync(new ChangeEventArgs { Value = "1" });
        Task create = cut.Find("[data-create-project]").ClickAsync(new MouseEventArgs());
        KanbanRequestTransmission request = Respond(r => new KanbanResponseTransmission { Project = new KanbanProjectView { Id = "new1", Name = r.Project!.Name } });
        await create;

        Assert.Equal(["Backlog", "Working", "Review", "Done", "Aborted"], request.Project!.Columns.Select(c => c.Name));
        Assert.EndsWith("/Projects/new1", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Markdown_KeepsScriptsAndHtmlOut()
    {
        string html = Markdown.ToHtml("[x](javascript:alert(1)) [ok](https://example.com) <b>raw</b> [rel](Projects/p1)").Value;

        Assert.DoesNotContain("javascript:", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("href=\"Projects/p1\"", html);
        Assert.Contains("&lt;b&gt;raw&lt;/b&gt;", html);
        Assert.False(Markdown.IsSafe("JavaScript:alert(1)"));
        Assert.True(Markdown.IsSafe("mailto:me@example.com"));
    }
}
