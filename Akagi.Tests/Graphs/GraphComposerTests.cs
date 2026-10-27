using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;
using Akagi.Graphs;
using Akagi.Tests.Support;
using System.Text.Json;
using static Akagi.Tests.Support.TestGraphs;

namespace Akagi.Tests.Graphs;

public class GraphComposerTests
{
    private static async Task<GraphCompositionResult> Compose(TempDirectory directory, string file)
    {
        string path = Path.Combine(directory.Path, file);
        GraphComposer composer = new(new FileGraphResolver());
        return await composer.ComposeAsync(GraphData.Load(path), Path.GetDirectoryName(path), path);
    }

    private static string? NameOf(GraphData graph, int index) => graph.Nodes[index].DisplayName;

    [Fact]
    public async Task References_AreInlinedAndRewired()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor"));
        directory.Write("Health.json", CharacterGraph("Health"));
        directory.Write("Group.json", GroupOfReferences("Tutor.json", "Health.json"));

        GraphCompositionResult result = await Compose(directory, "Group.json");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        GraphData graph = result.Graph;
        Assert.False(GraphComposer.ContainsReferences(graph));
        Assert.Equal(3 + 6 + 6, graph.Nodes.Count);

        int groupIndex = result.IndexMap[0];
        int tutorIndex = result.IndexMap[2];
        int healthIndex = result.IndexMap[3];
        Assert.Equal("Tutor", NameOf(graph, tutorIndex));
        Assert.Equal("Health", NameOf(graph, healthIndex));

        Assert.Contains(graph.Connections, c => c.SourceNodeIndex == tutorIndex && c.TargetNodeIndex == groupIndex && c.TargetArrayIndex == 0);
        Assert.Contains(graph.Connections, c => c.SourceNodeIndex == healthIndex && c.TargetNodeIndex == groupIndex && c.TargetArrayIndex == 1);
        int strategyIndex = result.IndexMap[4];
        Assert.Contains(graph.Connections, c => c.SourceNodeIndex == tutorIndex && c.TargetNodeIndex == strategyIndex
            && c.TargetConnectorName == nameof(OrchestratorStrategy.ManagerCharacterId));

        // The subgraph's own wiring survives with shifted indices
        Assert.Equal(5 + 5 + 5, graph.Connections.Count);
        Assert.Contains("Tutor.json", result.Origins[tutorIndex]);
    }

    [Fact]
    public async Task SameFileReferencedTwice_CreatesTwoCopies()
    {
        using TempDirectory directory = new();
        directory.Write("Worker.json", CharacterGraph("Worker"));
        directory.Write("Group.json", GroupOfReferences("Worker.json", "Worker.json"));

        GraphCompositionResult result = await Compose(directory, "Group.json");

        Assert.True(result.Success);
        Assert.NotEqual(result.IndexMap[2], result.IndexMap[3]);
        Assert.Equal(2, result.Graph.Nodes.Count(n => n.ShortTypeName == nameof(Character)));
    }

    [Fact]
    public async Task Overrides_ChangeReferencedNodeProperties()
    {
        using TempDirectory directory = new();
        directory.Write("Worker.json", CharacterGraph("Worker"));
        directory.Write("Group.json", GroupOfReferences("Worker.json", "Worker.json", managerReferenceProperties: new
        {
            GraphPath = "Worker.json",
            Overrides = new[] { "Worker.Name=Akagi", "#3.MaxSteps=7", "#0.Name=\"Quoted Card\"" }
        }));

        GraphCompositionResult result = await Compose(directory, "Group.json");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        int manager = result.IndexMap[2];
        int worker = result.IndexMap[3];
        Assert.Equal("Akagi", NameOf(result.Graph, manager));
        Assert.Equal("Worker", NameOf(result.Graph, worker));
        Assert.Contains(result.Graph.Nodes, n => n.DisplayName == "Quoted Card");

        NodeData puppeteer = result.Graph.Nodes.First(n => n.ShortTypeName == "SinglePuppeteer");
        Assert.Equal(7, ((JsonElement)puppeteer.Properties["MaxSteps"]!).GetInt32());
    }

    [Fact]
    public async Task BadOverride_IsReported()
    {
        using TempDirectory directory = new();
        directory.Write("Worker.json", CharacterGraph("Worker"));
        directory.Write("Group.json", GroupOfReferences("Worker.json", "Worker.json", managerReferenceProperties: new
        {
            GraphPath = "Worker.json",
            Overrides = new[] { "Nobody.Name=X", "Worker.NoSuchProperty=1", "garbage" }
        }));

        GraphCompositionResult result = await Compose(directory, "Group.json");

        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public async Task ReferenceCycle_IsDetected()
    {
        using TempDirectory directory = new();
        directory.Write("A.json", GroupOfReferences("B.json", "B.json"));
        // B exports a character but also pulls A back in through a second member
        GraphData b = CharacterGraph("B");
        b.Nodes.Add(Node<CharacterGraphReference>(new { GraphPath = "A.json" }));
        b.Nodes.Add(Node<CharacterGroup>(new { Name = "Loop" }, new Dictionary<string, int> { ["MemberCharacterIds"] = 1 }));
        b.Connections.Add(Connect(6, 7, nameof(CharacterGroup.MemberCharacterIds), 0));
        directory.Write("B.json", b);

        GraphCompositionResult result = await Compose(directory, "A.json");

        Assert.Contains(result.Errors, e => e.Contains("cycle"));
    }

    [Fact]
    public async Task AmbiguousExport_RequiresExportNode()
    {
        using TempDirectory directory = new();
        GraphData two = CharacterGraph("One");
        GraphData other = CharacterGraph("Two");
        int offset = two.Nodes.Count;
        two.Nodes.AddRange(other.Nodes);
        two.Connections.AddRange(other.Connections.Select(c => Connect(c.SourceNodeIndex + offset, c.TargetNodeIndex + offset, c.TargetConnectorName)));
        directory.Write("Two.json", two);
        directory.Write("Worker.json", CharacterGraph("Worker"));

        directory.Write("Ambiguous.json", GroupOfReferences("Two.json", "Worker.json"));
        GraphCompositionResult ambiguous = await Compose(directory, "Ambiguous.json");
        Assert.Contains(ambiguous.Errors, e => e.Contains("set ExportNode"));

        directory.Write("Picked.json", GroupOfReferences("Two.json", "Worker.json", managerReferenceProperties: new { GraphPath = "Two.json", ExportNode = "Two" }));
        GraphCompositionResult picked = await Compose(directory, "Picked.json");
        Assert.True(picked.Success, string.Join(Environment.NewLine, picked.Errors));
        Assert.Equal("Two", NameOf(picked.Graph, picked.IndexMap[2]));
    }

    [Fact]
    public async Task ReferenceByGraphId_IsFoundInSameDirectory()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor", "tutor-graph"));
        directory.Write("Worker.json", CharacterGraph("Worker"));
        directory.Write("Group.json", GroupOfReferences("", "Worker.json", managerReferenceProperties: new { GraphId = "tutor-graph" }));

        GraphCompositionResult result = await Compose(directory, "Group.json");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("Tutor", NameOf(result.Graph, result.IndexMap[2]));
    }

    [Fact]
    public async Task NestedReferences_ResolveRelativeToTheirOwnFile()
    {
        using TempDirectory directory = new();
        directory.Write(Path.Combine("agents", "Tutor.json"), CharacterGraph("Tutor"));
        directory.Write(Path.Combine("agents", "Worker.json"), CharacterGraph("Worker"));
        // Inner group lives next to its members and references them relatively
        directory.Write(Path.Combine("agents", "Inner.json"), GroupOfReferences("Tutor.json", "Worker.json"));

        GraphData outer = CharacterGraph("Outer");
        outer.Nodes.Add(Node<CharacterGraphReference>(new { GraphPath = "agents/Inner.json", ExportNode = "Tutor" }));
        outer.Nodes.Add(Node<CharacterGroup>(new { Name = "Outer" }, new Dictionary<string, int> { ["MemberCharacterIds"] = 2 }));
        outer.Connections.Add(Connect(1, 7, nameof(CharacterGroup.MemberCharacterIds), 0));
        outer.Connections.Add(Connect(6, 7, nameof(CharacterGroup.MemberCharacterIds), 1));
        directory.Write("Outer.json", outer);

        GraphCompositionResult result = await Compose(directory, "Outer.json");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("Tutor", NameOf(result.Graph, result.IndexMap[6]));
    }
}
