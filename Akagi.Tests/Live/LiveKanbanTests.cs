using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Data;
using Akagi.Kanban;
using Akagi.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Akagi.Tests.Live;

/// <summary>
/// Projects and tickets stored in a real MongoDB: ids, enums, days and the queries by user and project survive the
/// round trip. Runs only with AKAGI_LIVE_MONGO, see <see cref="LiveMongoFactAttribute"/>.
/// </summary>
public class LiveKanbanTests
{
    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [LiveMongoFact]
    public async Task Projects_SurviveTheRoundTripThroughMongo()
    {
        string database = "AkagiKanban_" + Guid.NewGuid().ToString("N")[..8];
        string connectionString = Environment.GetEnvironmentVariable("AKAGI_LIVE_MONGO")!.TrimEnd('/') + "/" + database;
        Monitor<DatabaseOptions> options = new(new DatabaseOptions { ConnectionString = connectionString });
        KanbanProjectDatabase projects = new(options);
        KanbanTicketDatabase tickets = new(options);
        KanbanService service = new(projects, tickets);
        string userId = ObjectId.GenerateNewId().ToString();
        string otherUser = ObjectId.GenerateNewId().ToString();
        try
        {
            KanbanProject project = await service.CreateProjectAsync(userId, "Akagi", "## Goal\nA companion.", KanbanLookup.ParseColumns(
                "Backlog: todo, Working: in progress, Review: in progress, Done: done, Aborted: done"));
            await service.CreateProjectAsync(otherUser, "Akagi");
            KanbanTicket model = await service.CreateTicketAsync(userId, project.Id!, "Data model", "Write **records**.", requiredDays: 1.5);
            KanbanTicket board = await service.CreateTicketAsync(userId, project.Id!, "Board page", requiredDays: 2,
                notBefore: new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc));
            await service.LinkAsync(userId, model.Id!, board.Id!);
            await service.MoveTicketAsync(userId, model.Id!, project.Columns[1].Id);
            await service.AddUpdateAsync(userId, model.Id!, "Ren", "Records written", 0.5);

            // Read back from Mongo, not from the objects above
            KanbanBoard loaded = await new KanbanService(new KanbanProjectDatabase(options), new KanbanTicketDatabase(options)).LoadAsync(userId, project.Id!);
            Assert.Equal(["Backlog", "Working", "Review", "Done", "Aborted"], loaded.Project.Columns.Select(c => c.Name));
            Assert.Equal(KanbanColumnKind.InProgress, loaded.Project.Columns[2].Kind);
            Assert.Equal(3, loaded.Project.NextNumber);
            KanbanTicket storedModel = loaded.Tickets.Single(t => t.Number == 1);
            KanbanTicket storedBoard = loaded.Tickets.Single(t => t.Number == 2);
            Assert.Equal(project.Columns[1].Id, storedModel.ColumnId);
            Assert.NotNull(storedModel.StartedAt);
            Assert.Equal(0.5, storedModel.RemainingDays);
            Assert.Equal(1.5, storedModel.RequiredDays);
            Assert.Equal("Ren", Assert.Single(storedModel.Updates).Author);
            Assert.Equal([storedModel.Id!], storedBoard.After);
            Assert.Equal(new DateTime(2026, 11, 18), storedBoard.NotBefore);
            Assert.Equal(DateTimeKind.Utc, storedBoard.NotBefore!.Value.Kind);
            Assert.Single(await projects.GetForUser(userId));

            // The website's view of it over the socket handler
            KanbanRequestHandler handler = new(service, new InMemoryLifeProfileDatabase(), NullLogger<KanbanRequestHandler>.Instance);
            KanbanResponseTransmission list = await handler.ProcessAsync(userId, "Tester", new KanbanRequestTransmission { Action = KanbanAction.ListProjects });
            KanbanProjectView summary = Assert.Single(list.Projects);
            Assert.Equal(2, summary.OpenTickets);
            Assert.Equal(2.5, summary.RemainingDays);

            await service.DeleteProjectAsync(userId, project.Id!);
            Assert.Empty(await tickets.GetForProject(project.Id!));
            Assert.Empty(await projects.GetForUser(userId));
            Assert.Single(await projects.GetForUser(otherUser));
        }
        finally
        {
            await new MongoClient(connectionString).DropDatabaseAsync(database);
        }
    }
}
