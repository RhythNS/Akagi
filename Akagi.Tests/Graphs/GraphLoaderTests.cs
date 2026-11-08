using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;
using Akagi.Characters.Memories;
using Akagi.Characters.TriggerPoints;
using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Data;
using Akagi.Graphs;
using Akagi.Tests.Support;
using Akagi.Users;
using static Akagi.Tests.Support.TestGraphs;

namespace Akagi.Tests.Graphs;

public class GraphLoaderTests
{
    private const string UserId = "65f000000000000000000001";

    [Fact]
    public async Task Create_ComposedGroup_WiresMembersAndManager()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor"));
        directory.Write("Health.json", CharacterGraph("Health"));
        directory.Write("Group.json", GroupOfReferences("Tutor.json", "Health.json"));

        InMemoryDatabaseFactory database = new();
        GraphLoader loader = new(database)
        {
            Resolver = new FileGraphResolver(),
            BaseDirectory = directory.Path
        };
        loader.LoadGraph(GraphData.Load(Path.Combine(directory.Path, "Group.json")));

        List<Savable> created = await loader.Create(UserId, "companion");

        CharacterGroup group = Assert.Single(created.OfType<CharacterGroup>());
        Character tutor = Assert.Single(created.OfType<Character>(), c => c.Name == "Tutor");
        Character health = Assert.Single(created.OfType<Character>(), c => c.Name == "Health");
        OrchestratorStrategy strategy = Assert.Single(created.OfType<OrchestratorStrategy>());

        Assert.Equal([tutor.Id!, health.Id!], group.MemberCharacterIds);
        Assert.Equal(strategy.Id, group.StrategyId);
        Assert.Equal(tutor.Id, strategy.ManagerCharacterId);
        Assert.Equal(UserId, group.UserId);
        Assert.Equal(UserId, tutor.UserId);
        Assert.NotEqual(tutor.CardId, health.CardId);
    }

    [Fact]
    public async Task Create_CharacterWithoutCard_FailsBeforeSavingAnything()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Connections.RemoveAll(c => c.TargetConnectorName == nameof(Character.CardId));
        InMemoryDatabaseFactory database = new();
        GraphLoader loader = new(database);
        loader.LoadGraph(graph);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => loader.Create(UserId, "x"));

        Assert.Contains("Akagi has no Card", error.Message);
        Assert.Empty(database.Documents);
    }

    [Fact]
    public async Task Create_WithReferencesButNoResolver_Throws()
    {
        GraphLoader loader = new(new InMemoryDatabaseFactory());
        loader.LoadGraph(GroupOfReferences("Tutor.json", "Health.json"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => loader.Create(UserId, "x"));
    }

    [Fact]
    public async Task Create_DoesNotPersistUserPlaceholders()
    {
        InMemoryDatabaseFactory database = new();
        GraphLoader loader = new(database);
        loader.LoadGraph(CharacterGraph("Akagi"));

        await loader.Create(UserId, "akagi");

        Assert.Empty(database.All<User>());
        Character character = Assert.Single(database.All<Character>());
        Assert.Equal(UserId, character.UserId);
        GraphInstance instance = Assert.Single(database.GraphInstances.Items);
        Assert.All(instance.SavableInfos, info => Assert.True(info.NodeIndex >= 0));
        Assert.DoesNotContain(instance.SavableInfos, info => info.NodeIndex == 2);
    }

    [Fact]
    public async Task Update_ReusesDocumentsByNodeIndex()
    {
        InMemoryDatabaseFactory database = new();
        GraphData graph = CharacterGraph("Akagi", "graph-1");
        GraphLoader loader = new(database);
        loader.LoadGraph(graph);
        List<Savable> created = await loader.Create(UserId, "akagi");
        string characterId = created.OfType<Character>().Single().Id!;

        graph.Nodes[1] = Node<Character>(new { Name = "Akagi Renamed" });
        GraphLoader updater = new(database);
        updater.LoadGraph(graph);
        List<Savable> updated = await updater.Update(UserId, "akagi");

        Character character = Assert.Single(updated.OfType<Character>());
        Assert.Equal(characterId, character.Id);
        Assert.Equal("Akagi Renamed", character.Name);
        Assert.Single(database.All<Character>());
    }

    [Fact]
    public async Task Update_KeepsConversationsAndMemory_EvenWhenTheFileCarriesEmptyOnes()
    {
        InMemoryDatabaseFactory database = new();
        GraphData graph = CharacterGraph("Akagi", "graph-1");
        GraphLoader loader = new(database);
        loader.LoadGraph(graph);
        await loader.Create(UserId, "akagi");

        Character lived = Assert.Single(database.All<Character>());
        lived.Conversations.GetCurrentConversation().AddMessage(new TextMessage { CharacterId = null, From = Message.Type.User, Text = "Hey", Time = DateTime.UtcNow });
        lived.Memory.LongTerm.AddThought(new SingleFactThought { Fact = "Likes jump rope" });
        await database.TrySave(lived);

        // What an editor that saved every property wrote: empty state next to the real settings
        graph.Nodes[1] = Node<Character>(new
        {
            Dirty = true,
            Name = "Akagi Renamed",
            Conversations = new { List = Array.Empty<object>(), Dirty = false, Current = (object?)null, ActiveSession = (object?)null },
            Memory = new { Goals = new { Thoughts = Array.Empty<object>() }, ShortTerm = new { Thoughts = Array.Empty<object>() }, LongTerm = new { Thoughts = Array.Empty<object>() }, Conversations = new { Thoughts = Array.Empty<object>() } }
        });
        GraphLoader updater = new(database);
        updater.LoadGraph(graph);
        Character character = Assert.Single((await updater.Update(UserId, "akagi")).OfType<Character>());

        Assert.Equal("Akagi Renamed", character.Name);
        Assert.Equal("Hey", Assert.IsType<TextMessage>(Assert.Single(Assert.Single(character.Conversations.List).Messages)).Text);
        Assert.Equal("Likes jump rope", Assert.Single(character.Memory.LongTerm.Thoughts).Fact);
    }

    [Fact]
    public void Runtime_state_is_never_a_graph_setting()
    {
        string[] state = [.. GraphNodeUtils.GetValueProperties(typeof(Character)).Concat(GraphNodeUtils.GetValueProperties(typeof(CharacterGroup)))
            .Where(p => !GraphNodeUtils.IsEditorVisible(p)).Select(p => p.DeclaringType!.Name + "." + p.Name).Distinct()];

        Assert.Contains("Character.Conversations", state);
        Assert.Contains("Character.Memory", state);
        Assert.Contains("CharacterGroup.Conversations", state);
        Assert.Contains("CharacterGroup.SharedMemory", state);
    }

    [Fact]
    public void MapExistingSavables_LegacyInstances_SkipNonSavableNodes()
    {
        // Node 1 (TriggerActionEntry) is not a Savable; legacy instances listed only savables, in node order.
        GraphData graph = new()
        {
            Nodes =
            [
                Node<TriggerPoint>(new { Name = "tp" }),
                Node<TriggerPoint.TriggerActionEntry>(),
                Node<TriggerReflect>(new { Name = "reflect" }),
            ]
        };
        GraphInstance legacy = new()
        {
            SavableInfos =
            [
                new GraphInstance.SavableInfo { SavableId = "a", CollectionName = "trigger_points" },
                new GraphInstance.SavableInfo { SavableId = "b", CollectionName = "trigger_actions" },
            ]
        };

        Dictionary<int, GraphInstance.SavableInfo> map = GraphLoader.MapExistingSavables(graph, legacy);

        Assert.Equal("a", map[0].SavableId);
        Assert.False(map.ContainsKey(1));
        Assert.Equal("b", map[2].SavableId);
    }

    [Fact]
    public void MapExistingSavables_UsesStoredNodeIndices()
    {
        GraphData graph = CharacterGraph("Akagi");
        GraphInstance instance = new()
        {
            SavableInfos =
            [
                new GraphInstance.SavableInfo { SavableId = "x", CollectionName = "c", NodeIndex = 4 },
                new GraphInstance.SavableInfo { SavableId = "y", CollectionName = "c", NodeIndex = 1 },
            ]
        };

        Dictionary<int, GraphInstance.SavableInfo> map = GraphLoader.MapExistingSavables(graph, instance);

        Assert.Equal("x", map[4].SavableId);
        Assert.Equal("y", map[1].SavableId);
        Assert.Equal(2, map.Count);
    }
}
