using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Bridge.Kanban;
using Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Kanban;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Kanban;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Command = Akagi.Receivers.Commands.Command;

namespace Akagi.Tests.Kanban;

internal class InMemoryKanbanProjectDatabase : InMemoryDatabase<KanbanProject>, IKanbanProjectDatabase
{
    public InMemoryKanbanProjectDatabase() : base("kanban_projects")
    {
    }

    public Task<List<KanbanProject>> GetForUser(string userId) => Task.FromResult(Items.Where(p => p.UserId == userId).ToList());
}

internal class InMemoryKanbanTicketDatabase : InMemoryDatabase<KanbanTicket>, IKanbanTicketDatabase
{
    public InMemoryKanbanTicketDatabase() : base("kanban_tickets")
    {
    }

    public Task<List<KanbanTicket>> GetForProject(string projectId) => Task.FromResult(Items.Where(t => t.ProjectId == projectId).ToList());

    public Task DeleteForProject(string projectId)
    {
        foreach (KanbanTicket ticket in Items.Where(t => t.ProjectId == projectId).ToList())
        {
            DeleteDocumentByIdAsync(ticket.Id!);
        }
        return Task.CompletedTask;
    }
}

internal class KanbanTestContext : LifeTestContext
{
    public InMemoryKanbanProjectDatabase Projects { get; } = new();
    public InMemoryKanbanTicketDatabase Tickets { get; } = new();
    public KanbanService Service { get; }

    public KanbanTestContext()
    {
        Database.Register(Projects);
        Database.Register(Tickets);
        Service = new KanbanService(Projects, Tickets);
    }

    public string UserId => User.Id!;

    public KanbanTicket Ticket(int number) => Tickets.Items.Single(t => t.Number == number);
}

public class KanbanSchedulerTests
{
    // A Monday
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static KanbanProjectView Project(int parallel = 1) => new()
    {
        Id = "p",
        Name = "Akagi",
        Parallel = parallel,
        Columns =
        [
            new KanbanColumnView { Id = "todo", Name = "Backlog", Kind = KanbanColumnKind.Todo },
            new KanbanColumnView { Id = "doing", Name = "Working", Kind = KanbanColumnKind.InProgress },
            new KanbanColumnView { Id = "done", Name = "Done", Kind = KanbanColumnKind.Done }
        ]
    };

    private static KanbanTicketView Ticket(string id, double days, double rank, string column = "todo", params string[] after) => new()
    {
        Id = id,
        Number = rank > 0 ? (int)rank : 0,
        Title = id,
        ColumnId = column,
        Rank = rank,
        RequiredDays = days,
        RemainingDays = days,
        After = after
    };

    [Fact]
    public void Calendar_SkipsDaysOffBothWays()
    {
        KanbanCalendar calendar = new(Monday, KanbanProjectView.WeekDays);

        // Five days of work end on Friday evening, not Monday morning
        Assert.Equal(5, calendar.EndToCalendar(5));
        Assert.Equal(new DateOnly(2026, 11, 20), calendar.EndDate(5));
        // The sixth work day is the next Monday
        Assert.Equal(7, calendar.StartToCalendar(5));
        Assert.Equal(7.5, calendar.StartToCalendar(5.5));
        Assert.Equal(5.5, calendar.CalendarToWork(7.5));
        // A Saturday counts as the end of Friday
        Assert.Equal(5, calendar.CalendarToWork(5.5));
        Assert.Equal(5, calendar.WorkAt(new DateOnly(2026, 11, 21)));
        Assert.Equal(5, calendar.CountWorkDays(Monday, new DateOnly(2026, 11, 22)));
    }

    [Fact]
    public void Calendar_WithoutWorkDays_CountsEveryDay()
    {
        KanbanCalendar calendar = new(Monday, 0);
        Assert.Equal(6, calendar.StartToCalendar(6));
    }

    [Fact]
    public void Plan_OnePersonWorksInProgressFirstThenByRank()
    {
        KanbanPlan plan = KanbanScheduler.Plan(Project(),
        [
            Ticket("b", 2, 1),
            Ticket("a", 1, 5, "doing"),
            Ticket("c", 3, 2),
            Ticket("old", 4, 0, "done")
        ], Monday);

        Assert.Equal(["a", "b", "c"], plan.Items.Where(i => !i.IsHistory).Select(i => i.Ticket.Id));
        Assert.Equal(6, plan.Finish);
        Assert.Equal(6, plan.RemainingDays);
        // Six work days from Monday end on the next Monday
        Assert.Equal(new DateOnly(2026, 11, 23), plan.FinishDate);
        KanbanPlannedTicket c = plan.Find("c")!;
        Assert.Equal(new DateOnly(2026, 11, 19), c.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 23), c.EndDate);
        Assert.Equal(3, c.CalendarStart);
        Assert.Equal(8, c.CalendarEnd);
        Assert.All(plan.Items, i => Assert.True(i.Critical));
    }

    [Fact]
    public void Plan_BlockerOfAnImportantTicketInheritsItsPriority()
    {
        // "first" is the most important but waits for "low", which would otherwise come last
        KanbanPlan plan = KanbanScheduler.Plan(Project(),
        [
            Ticket("first", 1, 1, "todo", "low"),
            Ticket("second", 1, 2),
            Ticket("low", 1, 9)
        ], Monday);

        Assert.Equal(["low", "first", "second"], plan.Items.Select(i => i.Ticket.Id));
        Assert.Equal(1, plan.Find("first")!.Start);
    }

    [Fact]
    public void Plan_TwoPeopleWorkInParallelAndWaitForDependencies()
    {
        KanbanPlan plan = KanbanScheduler.Plan(Project(parallel: 2),
        [
            Ticket("a", 3, 1),
            Ticket("b", 1, 2),
            Ticket("c", 1, 3, "todo", "a"),
            Ticket("d", 1, 4)
        ], Monday);

        Assert.Equal(0, plan.Find("a")!.Start);
        Assert.Equal(0, plan.Find("b")!.Start);
        Assert.Equal(1, plan.Find("d")!.Start);
        Assert.Equal(3, plan.Find("c")!.Start);
        Assert.Equal(4, plan.Finish);
        Assert.True(plan.Find("a")!.Critical);
        Assert.True(plan.Find("c")!.Critical);
        Assert.False(plan.Find("d")!.Critical);
    }

    [Fact]
    public void Plan_NotBeforeLeavesTheSlotToOtherWork()
    {
        KanbanTicketView later = Ticket("later", 1, 1);
        later.NotBefore = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);
        KanbanPlan plan = KanbanScheduler.Plan(Project(), [later, Ticket("now", 1, 2)], Monday);

        Assert.Equal(0, plan.Find("now")!.Start);
        Assert.Equal(2, plan.Find("later")!.Start);
        Assert.Equal(new DateOnly(2026, 11, 18), plan.Find("later")!.StartDate);
    }

    [Fact]
    public void Plan_BreaksCyclesAndFlagsThem()
    {
        KanbanPlan plan = KanbanScheduler.Plan(Project(),
        [
            Ticket("a", 1, 1, "todo", "b"),
            Ticket("b", 1, 2, "todo", "a")
        ], Monday);

        Assert.Equal(2, plan.Items.Count);
        Assert.Contains(plan.Items, i => i.InCycle);
        Assert.Equal(2, plan.Finish);
    }

    [Fact]
    public void Plan_StartsAtTheProjectStartAndShowsFinishedWork()
    {
        KanbanProjectView project = Project();
        project.StartDate = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        KanbanTicketView done = Ticket("done", 2, 1, "done");
        done.StartedAt = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        done.DoneAt = new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

        KanbanPlan plan = KanbanScheduler.Plan(project, [Ticket("a", 1, 1), done], Monday);

        Assert.Equal(new DateOnly(2026, 11, 23), plan.Calendar.Origin);
        Assert.Equal(new DateOnly(2026, 11, 23), plan.FinishDate);
        KanbanPlannedTicket history = plan.Find("done")!;
        Assert.True(history.IsHistory);
        Assert.Equal(-13, history.CalendarStart);
        Assert.Equal(-10, history.CalendarEnd);
    }

    [Fact]
    public void Plan_EmptyProjectHasNoFinish()
    {
        KanbanPlan plan = KanbanScheduler.Plan(Project(), [], Monday);
        Assert.Null(plan.FinishDate);
        Assert.Empty(plan.Items);
    }
}

public class KanbanServiceTests
{
    [Fact]
    public async Task Project_NeedsEveryKindOfColumn()
    {
        KanbanTestContext test = new();

        KanbanException missing = await Assert.ThrowsAsync<KanbanException>(() => test.Service.CreateProjectAsync(test.UserId, "Akagi", columns:
            [new KanbanColumn { Name = "Backlog", Kind = KanbanColumnKind.Todo }, new KanbanColumn { Name = "Done", Kind = KanbanColumnKind.Done }]));
        Assert.Contains("in progress", missing.Message);

        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi", columns: KanbanLookup.ParseColumns(
            "Backlog: todo, Working: in progress, Review: in progress, Done: done, Aborted: done"));
        Assert.Equal(["Backlog", "Working", "Review", "Done", "Aborted"], project.Columns.Select(c => c.Name));
        Assert.Equal(2, project.Columns.Count(c => c.Kind == KanbanColumnKind.InProgress));

        await Assert.ThrowsAsync<KanbanException>(() => test.Service.CreateProjectAsync(test.UserId, "akagi"));
    }

    [Fact]
    public async Task RemovingAColumn_MovesItsTicketsToTheSameKind()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi", columns: KanbanLookup.ParseColumns(
            "Backlog: todo, Working: in progress, Review: in progress, Done: done"));
        KanbanColumn review = project.Columns[2];
        KanbanTicket ticket = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "Login page", columnId: review.Id);

        KanbanProjectView view = KanbanViews.ToView(project);
        view.Columns = [.. view.Columns.Where(c => c.Id != review.Id)];
        await test.Service.SaveProjectAsync(test.UserId, view);

        Assert.Equal(project.Columns[1].Id, test.Ticket(1).ColumnId);

        view.Columns = [.. view.Columns.Where(c => c.Kind != KanbanColumnKind.InProgress)];
        await Assert.ThrowsAsync<KanbanException>(() => test.Service.SaveProjectAsync(test.UserId, view));
    }

    [Fact]
    public async Task Columns_NoteStartAndFinishAndRestoreTheEstimate()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        KanbanTicket ticket = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "Login page", requiredDays: 3);
        Assert.Equal(1, ticket.Number);
        Assert.Null(ticket.StartedAt);

        await test.Service.MoveTicketAsync(test.UserId, ticket.Id!, project.FirstOf(KanbanColumnKind.InProgress).Id);
        Assert.NotNull(test.Ticket(1).StartedAt);

        await test.Service.MoveTicketAsync(test.UserId, ticket.Id!, project.FirstOf(KanbanColumnKind.Done).Id);
        Assert.NotNull(test.Ticket(1).DoneAt);
        Assert.Equal(0, test.Ticket(1).RemainingDays);

        await test.Service.MoveTicketAsync(test.UserId, ticket.Id!, project.FirstOf(KanbanColumnKind.Todo).Id);
        Assert.Null(test.Ticket(1).DoneAt);
        Assert.Equal(3, test.Ticket(1).RemainingDays);
    }

    [Fact]
    public async Task Estimate_RemainingFollowsRequiredUntilWorkStarts()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        KanbanTicket ticket = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "Login page", requiredDays: 2);

        await test.Service.UpdateTicketAsync(test.UserId, ticket.Id!, new KanbanTicketChange { RequiredDays = 4 });
        Assert.Equal(4, test.Ticket(1).RemainingDays);

        await test.Service.MoveTicketAsync(test.UserId, ticket.Id!, project.FirstOf(KanbanColumnKind.InProgress).Id);
        await test.Service.UpdateTicketAsync(test.UserId, ticket.Id!, new KanbanTicketChange { RequiredDays = 5 });
        Assert.Equal(4, test.Ticket(1).RemainingDays);

        await Assert.ThrowsAsync<KanbanException>(() => test.Service.UpdateTicketAsync(test.UserId, ticket.Id!, new KanbanTicketChange { RemainingDays = -1 }));
    }

    [Fact]
    public async Task Links_RefuseCyclesAndDisappearWithTheTicket()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        KanbanTicket a = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "A");
        KanbanTicket b = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "B");
        KanbanTicket c = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "C");

        await test.Service.LinkAsync(test.UserId, a.Id!, b.Id!);
        await test.Service.LinkAsync(test.UserId, b.Id!, c.Id!);
        KanbanException cycle = await Assert.ThrowsAsync<KanbanException>(() => test.Service.LinkAsync(test.UserId, c.Id!, a.Id!));
        Assert.Contains("#3 already waits for #1", cycle.Message);
        await Assert.ThrowsAsync<KanbanException>(() => test.Service.LinkAsync(test.UserId, a.Id!, a.Id!));

        await test.Service.DeleteTicketAsync(test.UserId, b.Id!);
        Assert.Empty(test.Ticket(3).After);
    }

    [Fact]
    public async Task Reorder_KeepsOtherTicketsInPlace()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        List<KanbanTicket> tickets = [];
        foreach (string title in new[] { "A", "B", "C", "D" })
        {
            tickets.Add(await test.Service.CreateTicketAsync(test.UserId, project.Id!, title));
        }

        // Swap B and D; A and C keep their places
        KanbanBoard board = await test.Service.ReorderAsync(test.UserId, project.Id!, [tickets[3].Id!, tickets[1].Id!]);

        Assert.Equal(["A", "D", "C", "B"], board.Tickets.OrderBy(t => t.Rank).Select(t => t.Title));
    }

    [Fact]
    public async Task Tickets_OfOtherUsersAreNotFound()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        KanbanTicket ticket = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "A");

        string stranger = ObjectId.GenerateNewId().ToString();
        await Assert.ThrowsAsync<KanbanException>(() => test.Service.LoadAsync(stranger, project.Id!));
        await Assert.ThrowsAsync<KanbanException>(() => test.Service.AddUpdateAsync(stranger, ticket.Id!, "x", "hi"));
    }

    [Fact]
    public async Task Updates_CarryAuthorAndRemainingDays()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        KanbanTicket ticket = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "A", requiredDays: 3);

        (_, KanbanTicket changed) = await test.Service.AddUpdateAsync(test.UserId, ticket.Id!, "Ren", "Layout **done**", 1.5);
        KanbanUpdate update = Assert.Single(changed.Updates);
        Assert.Equal("Ren", update.Author);
        Assert.Equal(1.5, update.RemainingDays);
        Assert.Equal(1.5, test.Ticket(1).RemainingDays);

        await test.Service.EditUpdateAsync(test.UserId, ticket.Id!, update.Id, "Layout done, styles next");
        Assert.Equal("Layout done, styles next", test.Ticket(1).Updates[0].Text);
        await test.Service.DeleteUpdateAsync(test.UserId, ticket.Id!, update.Id);
        Assert.Empty(test.Ticket(1).Updates);
    }
}

public class KanbanToolTests
{
    private static async Task<KanbanTestContext> WithProject()
    {
        KanbanTestContext test = new();
        await test.Service.CreateProjectAsync(test.UserId, "Akagi", "Personal AI companion.", KanbanLookup.ParseColumns(
            "Backlog: todo, Working: in progress, Review: in progress, Done: done, Aborted: done"));
        return test;
    }

    [Fact]
    public async Task Tools_PlanAProjectFromCreationToFinish()
    {
        KanbanTestContext test = await WithProject();

        string created = await test.Run<CreateTicketsCommand>(("Tickets", "Data model | 1 | Mongo records\nBoard page | 2\nGantt page | 3,5"));
        Assert.Contains("#1 Data model (1 day)", created);
        Assert.Contains("#3 Gantt page (3.5 days)", created);
        Assert.Equal("Mongo records", test.Ticket(1).Description);

        string linked = await test.Run<LinkTicketsCommand>(("Links", "#1 > #2, #1 > #3, #3 > #1"));
        Assert.Contains("Linked #1 > #2, #1 > #3", linked);
        Assert.Contains("Not done: #3 already waits for #1, so #1 cannot wait for #3.", linked);

        string moved = await test.Run<MoveTicketCommand>(("Ticket", "#1"), ("Column", "done"));
        Assert.Contains("from Backlog to Done", moved);
        Assert.Contains("Ready to start now: #2 Board page, #3 Gantt page", moved);

        string update = await test.Run<AddTicketUpdateCommand>(("Ticket", "Board"), ("Text", "Columns render"), ("RemainingDays", 1));
        Assert.Contains("1 day left", update);
        Assert.Equal("Akagi", test.Ticket(2).Updates.Single().Author);

        string plan = await test.Run<ShowPlanCommand>();
        Assert.Contains("2 open tickets, 4.5 days of work", plan);
        Assert.Contains("#2 Board page", plan);

        string board = await test.Run<ShowBoardCommand>(("Project", "aka"));
        Assert.Contains("Backlog (to do, 2):", board);
        Assert.Contains("#2 Board page [1 day left of 2 days", board);
        Assert.Contains("About: Personal AI companion.", board);

        string ticket = await test.Run<ShowTicketCommand>(("Ticket", "#3"));
        Assert.Contains("Waits for: #1 Data model (done)", ticket);
        Assert.Contains("Description:", ticket);

        string list = await test.Run<ListProjectsCommand>();
        Assert.Contains("Akagi: 2 open of 3 tickets, 4.5 days of work left", list);
    }

    [Fact]
    public async Task Tools_ExplainWhatTheyCouldNotFind()
    {
        KanbanTestContext test = await WithProject();
        await test.Service.CreateProjectAsync(test.UserId, "Website");

        Assert.Contains("Name the project", await test.Run<ShowBoardCommand>());
        Assert.Contains("has no ticket #9", await test.Run<ShowTicketCommand>(("Project", "Akagi"), ("Ticket", "#9")));
        await test.Run<CreateTicketCommand>(("Project", "Akagi"), ("Title", "Login"), ("RequiredDays", 2));
        Assert.Contains("has no column 'Limbo'", await test.Run<MoveTicketCommand>(("Project", "Akagi"), ("Ticket", "#1"), ("Column", "Limbo")));
        Assert.Contains("Not done: nothing to change", await test.Run<UpdateTicketCommand>(("Project", "Akagi"), ("Ticket", "#1")));
    }

    [Fact]
    public async Task Tools_EditTheProjectKnowledge()
    {
        KanbanTestContext test = await WithProject();

        string output = await test.Run<EditProjectCommand>(("AppendToDescription", "Decided: Blazor for the web UI."), ("Parallel", 2), ("StartDate", "2026-12-01"));

        KanbanProject project = test.Projects.Items.Single();
        Assert.Contains("description extended", output);
        Assert.Equal("Personal AI companion." + Environment.NewLine + Environment.NewLine + "Decided: Blazor for the web UI.", project.Description);
        Assert.Equal(2, project.Parallel);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), project.StartDate);
    }

    [Fact]
    public async Task CreateProject_ParsesColumns()
    {
        KanbanTestContext test = new();
        string output = await test.Run<CreateProjectCommand>(("Name", "Website"), ("Columns", "Ideas: todo, Doing: progress, Shipped: done"));
        Assert.Contains("Ideas (to do), Doing (in progress), Shipped (done)", output);
        Assert.Contains("needs a type", await test.Run<CreateProjectCommand>(("Name", "Other"), ("Columns", "Ideas, Doing: progress")));
    }

    [Fact]
    public void WorkItemTools_RunForThePersonaOnlyWhenTheUserStartedTheTurn()
    {
        Command move = new MoveTicketCommand();
        Command mood = new RecordMoodCommand();
        Command board = new ShowBoardCommand();

        Assert.True(ToolGuard.IsOffered(move, TurnSource.Consultation, TurnSource.User));
        Assert.False(ToolGuard.IsOffered(move, TurnSource.Consultation, TurnSource.System));
        Assert.False(ToolGuard.IsOffered(move, TurnSource.System));
        Assert.False(ToolGuard.IsOffered(mood, TurnSource.Consultation, TurnSource.User));
        Assert.True(ToolGuard.IsOffered(board, TurnSource.System));
    }

    [Fact]
    public async Task WorkItemTools_HaveTheirOwnBudget()
    {
        KanbanTestContext test = await WithProject();
        test.Context.Turn = TurnSource.Consultation;
        test.Context.ConsultedOn = TurnSource.User;
        ToolGuard guard = new();

        for (int i = 0; i < ToolGuard.MaxWorkItemChangesPerStep; i++)
        {
            Assert.Null(guard.Check(Tool<CreateTicketCommand>(("Title", $"T{i}")), test.Context));
        }
        Assert.Contains("board changes per reply", guard.Check(Tool<CreateTicketCommand>(("Title", "one more")), test.Context));

        test.Context.ConsultedOn = TurnSource.System;
        Assert.Contains("not available during a consultation", new ToolGuard().Check(Tool<CreateTicketCommand>(("Title", "x")), test.Context));
    }

    private static T Tool<T>(params (string Name, string Value)[] arguments) where T : Command, new()
    {
        T command = new();
        Argument[] args = command.GetDefaultArguments();
        foreach ((string name, string value) in arguments)
        {
            args.Single(a => a.Name == name).Value = value;
        }
        command.Arguments = args;
        return command;
    }

    [Fact]
    public async Task Injection_TellsCharactersAboutTheProjects()
    {
        KanbanTestContext test = await WithProject();
        await test.Run<CreateTicketsCommand>(("Tickets", "Data model | 1\nBoard page | 2"));
        await test.Run<MoveTicketCommand>(("Ticket", "#1"), ("Column", "Working"));
        await test.Run<AddTicketUpdateCommand>(("Ticket", "#1"), ("Text", "Records written"));
        KanbanProject project = test.Projects.Items.Single();
        KanbanBoard board = await test.Service.LoadAsync(test.UserId, project.Id!);
        DateOnly monday = new(2026, 11, 16);

        string overview = KanbanInjectionCompiler.Render([board], monday, KanbanInjectionCompiler.DetailLevel.Overview, 3, 1500);
        Assert.Contains("Project Akagi: 2 open of 2 tickets, 3 days of work left, plan finishes Wed 2026-11-18.", overview);
        Assert.Contains("About: Personal AI companion.", overview);
        Assert.Contains("In progress: #1 Data model (Working, 1 day left)", overview);
        Assert.Contains("Next: #2 Board page", overview);

        string full = KanbanInjectionCompiler.Render([board], monday, KanbanInjectionCompiler.DetailLevel.Full, 3, 1500);
        Assert.Contains("Working (in progress, 1):", full);
        Assert.Contains("#1 Data model in detail:", full);
        Assert.Contains("by Akagi: Records written", full);

        Assert.Single(KanbanInjectionCompiler.Select(test.Projects.Items, "akagi, other"));
        Assert.Empty(KanbanInjectionCompiler.Select(test.Projects.Items, "other"));
    }
}

public class KanbanSocketTests
{
    [Fact]
    public async Task Website_CreatesPlansAndEditsAProject()
    {
        KanbanTestContext test = new();
        KanbanRequestHandler handler = new(test.Service, test.Profiles, NullLogger<KanbanRequestHandler>.Instance);

        KanbanResponseTransmission created = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission
        {
            RequestId = "1",
            Action = KanbanAction.SaveProject,
            Project = new KanbanProjectView
            {
                Name = "Akagi",
                Columns =
                [
                    new KanbanColumnView { Name = "Backlog", Kind = KanbanColumnKind.Todo },
                    new KanbanColumnView { Name = "Working", Kind = KanbanColumnKind.InProgress },
                    new KanbanColumnView { Name = "Done", Kind = KanbanColumnKind.Done }
                ],
                Parallel = 2
            }
        });
        Assert.Null(created.Error);
        Assert.Equal("1", created.RequestId);
        KanbanProjectView project = created.Project!;
        Assert.Equal(2, project.Parallel);
        Assert.All(project.Columns, c => Assert.NotEmpty(c.Id));

        KanbanResponseTransmission ticket = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission
        {
            Action = KanbanAction.SaveTicket,
            ProjectId = project.Id,
            Ticket = new KanbanTicketView { Title = "Login", Description = "## Goal", RequiredDays = 2, RemainingDays = 2 }
        });
        Assert.Null(ticket.Error);
        KanbanTicketView login = Assert.Single(ticket.Tickets);
        Assert.Equal(ticket.TicketId, login.Id);
        Assert.Equal(project.Columns[0].Id, login.ColumnId);

        KanbanResponseTransmission planned = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission
        {
            Action = KanbanAction.Plan,
            ProjectId = project.Id,
            Estimates = [new KanbanEstimateView { TicketId = login.Id, RequiredDays = 3.5, SetNotBefore = true, NotBefore = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) }]
        });
        Assert.Equal(3.5, planned.Tickets[0].RemainingDays);
        Assert.Equal(new DateTime(2026, 12, 1), planned.Tickets[0].NotBefore);

        KanbanResponseTransmission updated = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission
        {
            Action = KanbanAction.AddUpdate,
            TicketId = login.Id,
            Text = "Started on the form"
        });
        Assert.Equal("Tester", updated.Tickets[0].Updates.Single().Author);

        KanbanResponseTransmission list = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission { Action = KanbanAction.ListProjects });
        KanbanProjectView summary = Assert.Single(list.Projects);
        Assert.Equal(1, summary.OpenTickets);
        Assert.Equal(3.5, summary.RemainingDays);
        Assert.NotNull(summary.Finish);

        KanbanResponseTransmission peek = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission { Action = KanbanAction.Peek, ProjectId = project.Id });
        Assert.True(peek.Project!.UpdatedAt >= updated.Project!.UpdatedAt);
        Assert.Empty(peek.Tickets);

        KanbanResponseTransmission wrong = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission { Action = KanbanAction.Load, ProjectId = ObjectId.GenerateNewId().ToString() });
        Assert.Equal("That project does not exist.", wrong.Error);

        KanbanResponseTransmission deleted = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission { Action = KanbanAction.DeleteProject, ProjectId = project.Id });
        Assert.Empty(deleted.Projects);
        Assert.Empty(test.Tickets.Items);
    }

    [Fact]
    public async Task Website_MovesTicketsInColumnOrder()
    {
        KanbanTestContext test = new();
        KanbanProject project = await test.Service.CreateProjectAsync(test.UserId, "Akagi");
        KanbanTicket a = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "A");
        KanbanTicket b = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "B");
        KanbanTicket c = await test.Service.CreateTicketAsync(test.UserId, project.Id!, "C", columnId: project.FirstOf(KanbanColumnKind.InProgress).Id);
        KanbanRequestHandler handler = new(test.Service, test.Profiles, NullLogger<KanbanRequestHandler>.Instance);

        string working = project.FirstOf(KanbanColumnKind.InProgress).Id;
        KanbanResponseTransmission moved = await handler.ProcessAsync(test.UserId, "Tester", new KanbanRequestTransmission
        {
            Action = KanbanAction.MoveTicket,
            TicketId = b.Id!,
            ColumnId = working,
            OrderedIds = [b.Id!, c.Id!]
        });

        Assert.Null(moved.Error);
        Assert.Equal(["B", "C"], moved.Tickets.Where(t => t.ColumnId == working).OrderBy(t => t.Rank).Select(t => t.Title));
        Assert.NotNull(moved.Tickets.Single(t => t.Title == "B").StartedAt);
    }
}
