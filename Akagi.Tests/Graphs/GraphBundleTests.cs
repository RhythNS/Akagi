using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;
using Akagi.Communication;
using Akagi.Communication.Commands;
using Akagi.Communication.Commands.Savables;
using Akagi.Communication.TelegramComs;
using Akagi.Graphs;
using Akagi.Tests.Support;
using Akagi.Users;
using System.Text;
using static Akagi.Tests.Support.TestGraphs;

namespace Akagi.Tests.Graphs;

/// <summary>
/// Uploading a graph with all its subgraphs at once: bundles, order independent multi file uploads and Telegram albums.
/// </summary>
public class GraphBundleTests
{
    private const string UserId = "65f000000000000000000001";

    private static async Task<GraphData> Bundle(TempDirectory directory, string file)
    {
        string path = Path.Combine(directory.Path, file);
        GraphBundler.BundleResult result = await GraphBundler.BundleAsync(GraphData.Load(path), new FileGraphResolver(), Path.GetDirectoryName(path));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        return result.Bundle;
    }

    /// <summary>
    /// Group.json references sub/Nested.json (exporting its Health) and Tutor.json; Nested.json references
    /// ../Health.json and ../Tutor.json again.
    /// </summary>
    private static TempDirectory NestedGraphs()
    {
        TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor", "tutor-id"));
        directory.Write("Health.json", CharacterGraph("Health", "health-id"));
        directory.Write(Path.Combine("sub", "Nested.json"), GroupOfReferences("../Health.json", "../Tutor.json", "nested-id"));
        directory.Write("Group.json", GroupOfReferences("sub/Nested.json", "Tutor.json", "group-id",
            managerReferenceProperties: new { GraphPath = "sub/Nested.json", ExportNode = "Health" }));
        return directory;
    }

    [Fact]
    public async Task Bundle_EmbedsEveryReferencedGraphOnce()
    {
        using TempDirectory directory = NestedGraphs();

        GraphData bundle = await Bundle(directory, "Group.json");

        Assert.Equal("group-id", bundle.GraphId);
        Assert.NotNull(bundle.Embedded);
        Assert.Equal(["Health.json", "Tutor.json", "sub/Nested.json"], bundle.Embedded.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.All(bundle.Embedded, e => Assert.Null(e.Graph.Embedded));
    }

    [Fact]
    public async Task Bundle_ComposesWithoutAnyFiles_LikeTheOriginal()
    {
        using TempDirectory directory = NestedGraphs();
        string json = (await Bundle(directory, "Group.json")).ToJson();
        string groupPath = Path.Combine(directory.Path, "Group.json");
        GraphCompositionResult fromFiles = await new GraphComposer(new FileGraphResolver())
            .ComposeAsync(GraphData.Load(groupPath), directory.Path, groupPath);

        // No resolver and no base directory: everything has to come from the bundle itself
        GraphCompositionResult fromBundle = await new GraphComposer(new CompositeGraphResolver()).ComposeAsync(GraphData.Parse(json), null);

        Assert.True(fromBundle.Success, string.Join(Environment.NewLine, fromBundle.Errors));
        Assert.False(GraphComposer.ContainsReferences(fromBundle.Graph));
        Assert.Equal(fromFiles.Graph.Nodes.Count, fromBundle.Graph.Nodes.Count);
        Assert.Equal(fromFiles.Graph.Connections.Count, fromBundle.Graph.Connections.Count);
    }

    [Fact]
    public async Task Bundle_UnresolvableReference_ReportsError()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor"));
        string path = directory.Write("Group.json", GroupOfReferences("Tutor.json", "Missing.json"));

        GraphBundler.BundleResult result = await GraphBundler.BundleAsync(GraphData.Load(path), new FileGraphResolver(), directory.Path);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Missing.json"));
    }

    [Fact]
    public async Task Bundle_OfABundle_KeepsEmbeddedGraphs()
    {
        using TempDirectory directory = NestedGraphs();
        GraphData first = await Bundle(directory, "Group.json");
        using TempDirectory elsewhere = new();
        elsewhere.Write("Group.bundle.json", first);

        GraphData second = await Bundle(elsewhere, "Group.bundle.json");

        Assert.Equal(first.Embedded!.Select(e => e.Path).Order(), second.Embedded!.Select(e => e.Path).Order());
    }

    [Fact]
    public async Task Loader_CreatesBundleWithoutResolver()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor"));
        directory.Write("Health.json", CharacterGraph("Health"));
        directory.Write("Group.json", GroupOfReferences("Tutor.json", "Health.json"));
        GraphData bundle = GraphData.Parse((await Bundle(directory, "Group.json")).ToJson());

        GraphLoader loader = new(new InMemoryDatabaseFactory());
        loader.LoadGraph(bundle);
        List<Akagi.Data.Savable> created = await loader.Create(UserId, "companion");

        CharacterGroup group = Assert.Single(created.OfType<CharacterGroup>());
        Assert.Equal(2, group.MemberCharacterIds.Length);
        Assert.Equal(2, created.OfType<Character>().Count());
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(2, 1, 0)]
    [InlineData(1, 2, 0)]
    public void Plan_OnlyInstantiatesTheGraphNobodyReferences_InAnyOrder(int a, int b, int c)
    {
        GraphUploadPlan.UploadedGraph[] files =
        [
            new("Tutor.json", CharacterGraph("Tutor", "tutor-id")),
            new("Health.json", CharacterGraph("Health", "health-id")),
            new("Group.json", GroupOfReferences("Tutor.json", "Health.json", "group-id")),
        ];

        GraphUploadPlan plan = GraphUploadPlan.Create([files[a], files[b], files[c]]);

        Assert.Equal("Group.json", Assert.Single(plan.Roots).FileName);
        Assert.Equal(["Health.json", "Tutor.json"], plan.Subgraphs.Select(s => s.Graph.FileName).Order());
        Assert.All(plan.Subgraphs, s => Assert.Equal("Group.json", s.UsedBy));
        Assert.Equal(3, plan.Definitions.Count);
    }

    [Fact]
    public void Plan_InstantiateAll_KeepsEveryGraphARoot()
    {
        GraphUploadPlan plan = GraphUploadPlan.Create(
        [
            new("Tutor.json", CharacterGraph("Tutor")),
            new("Group.json", GroupOfReferences("Tutor.json", "Tutor.json")),
        ], instantiateAll: true);

        Assert.Equal(2, plan.Roots.Count);
        Assert.Empty(plan.Subgraphs);
    }

    [Fact]
    public async Task Plan_StoresEmbeddedGraphsAsDefinitions()
    {
        using TempDirectory directory = NestedGraphs();
        GraphData bundle = await Bundle(directory, "Group.json");

        GraphUploadPlan plan = GraphUploadPlan.Create([new("Group.bundle.json", bundle)]);

        Assert.Equal("Group.bundle.json", Assert.Single(plan.Roots).FileName);
        Assert.Equal(["Group.bundle.json", "Health.json", "Nested.json", "Tutor.json"], plan.Definitions.Select(d => d.FileName).Order());
        // The root is stored without the copies, which are stored on their own
        Assert.DoesNotContain("Embedded", plan.Definitions.Single(d => d.FileName == "Group.bundle.json").Json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadGraph_SubgraphsBeforeOrAfterTheGroup_CreatesOneGroup(bool groupFirst)
    {
        (InMemoryDatabaseFactory database, RecordingCommunicator communicator, UploadGraphCommand command) = UploadSetup();
        TestDocument group = new("Group.json", GroupOfReferences("Tutor.json", "Health.json"));
        TestDocument[] subgraphs = [new("Tutor.json", CharacterGraph("Tutor")), new("Health.json", CharacterGraph("Health"))];
        Document[] documents = groupFirst ? [group, .. subgraphs] : [.. subgraphs, group];

        CommandResult result = await command.ExecuteAsync(Context(database), documents, ["create", "companion"]);

        Assert.True(result.Success, string.Join(Environment.NewLine, communicator.Sent));
        CharacterGroup created = Assert.Single(database.All<CharacterGroup>());
        Assert.Equal(2, created.MemberCharacterIds.Length);
        // The subgraphs were not created as standalone characters on top of the group members
        Assert.Equal(2, database.All<Character>().Count());
        Assert.Contains("used by Group.json", communicator.Sent.Last());
    }

    [Fact]
    public async Task UploadGraph_Bundle_CreatesEverythingFromOneFile()
    {
        using TempDirectory directory = NestedGraphs();
        GraphData bundle = await Bundle(directory, "Group.json");
        (InMemoryDatabaseFactory database, RecordingCommunicator communicator, UploadGraphCommand command) = UploadSetup();

        CommandResult result = await command.ExecuteAsync(Context(database), [new TestDocument("Group.bundle.json", bundle)], ["create", "companion"]);

        Assert.True(result.Success, string.Join(Environment.NewLine, communicator.Sent));
        Assert.Equal(2, database.All<CharacterGroup>().Count());
        Assert.Equal(3, database.All<Character>().Count());
        Assert.Equal(4, database.GetDatabase<InMemoryGraphDefinitionDatabase>().Items.Count());
    }

    [Fact]
    public async Task AlbumCollector_HandsOverAllPartsOnce()
    {
        List<IReadOnlyList<int>> completed = [];
        AlbumCollector<int> collector = new(TimeSpan.FromMilliseconds(50), parts =>
        {
            lock (completed)
            {
                completed.Add(parts);
            }
            return Task.CompletedTask;
        });

        Task[] waits = [collector.Add("a", 1), collector.Add("b", 10), collector.Add("a", 2), collector.Add("a", 3)];
        await Task.WhenAll(waits);

        Assert.Equal(2, completed.Count);
        Assert.Contains(completed, parts => parts.SequenceEqual([1, 2, 3]));
        Assert.Contains(completed, parts => parts.SequenceEqual([10]));
        Assert.Equal(0, collector.Pending);
    }

    private static (InMemoryDatabaseFactory, RecordingCommunicator, UploadGraphCommand) UploadSetup()
    {
        InMemoryDatabaseFactory database = new();
        database.Register(new InMemoryGraphDefinitionDatabase());
        RecordingCommunicator communicator = new();
        UploadGraphCommand command = new(database);
        command.Init(communicator);
        return (database, communicator, command);
    }

    private static Command.Context Context(InMemoryDatabaseFactory database) => new()
    {
        DatabaseFactory = database,
        User = new User { Id = UserId, Name = "Tester" }
    };

    private class TestDocument : Document
    {
        private readonly string _json;

        public TestDocument(string name, GraphData graph)
        {
            Name = name;
            _json = graph.ToJson();
        }

        public override string Name { get; }

        public override Task<MemoryStream?> GetStream() => Task.FromResult<MemoryStream?>(new MemoryStream(Encoding.UTF8.GetBytes(_json)));
    }
}

internal class InMemoryGraphDefinitionDatabase : InMemoryDatabase<GraphDefinition>, IGraphDefinitionDatabase
{
    public InMemoryGraphDefinitionDatabase() : base("graph_definitions")
    {
    }

    public Task<GraphDefinition?> GetByGraphId(string graphId, string userId) =>
        Task.FromResult(Items.FirstOrDefault(d => d.GraphId == graphId && d.UserId == userId));

    public Task<GraphDefinition?> GetByFileName(string fileName, string userId) =>
        Task.FromResult(Items.FirstOrDefault(d => d.FileName == fileName && d.UserId == userId));

    public async Task Store(string graphId, string fileName, string json, string userId)
    {
        GraphDefinition definition = await GetByGraphId(graphId, userId) ?? new GraphDefinition { GraphId = graphId, UserId = userId };
        definition.FileName = fileName;
        definition.Json = json;
        await SaveDocumentAsync(definition);
    }
}
