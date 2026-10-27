using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Graphs;
using Akagi.Tests.Support;
using static Akagi.Tests.Support.TestGraphs;

namespace Akagi.Tests.Graphs;

public class GraphValidatorTests
{
    private static Task<GraphValidationReport> Validate(GraphData graph)
    {
        return new GraphValidator().ValidateAsync(graph, null, "test");
    }

    private static string[] Codes(GraphValidationReport report, GraphIssueSeverity? severity = null)
    {
        return [.. report.Issues.Where(i => severity == null || i.Severity == severity).Select(i => i.Code)];
    }

    [Fact]
    public async Task CompleteCharacterGraph_IsValidWithoutWarnings()
    {
        GraphValidationReport report = await Validate(CharacterGraph("Akagi"));

        Assert.True(report.IsValid, report.ToText());
        Assert.Empty(Codes(report, GraphIssueSeverity.Warning));
    }

    [Fact]
    public async Task UnknownType_IsError()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Nodes.Add(Node("Akagi.DoesNotExist, Akagi"));

        GraphValidationReport report = await Validate(graph);

        Assert.False(report.IsValid);
        GraphIssue issue = Assert.Single(report.Issues, i => i.Code == "node.unknown-type");
        Assert.Equal(6, issue.NodeIndex);
    }

    [Fact]
    public async Task DanglingConnectionIndices_AreErrors()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Connections.Add(Connect(42, 1, "CardId"));
        graph.Connections.Add(Connect(0, 99, "CardId"));

        GraphValidationReport report = await Validate(graph);

        Assert.Contains("connection.dangling-source", Codes(report, GraphIssueSeverity.Error));
        Assert.Contains("connection.dangling-target", Codes(report, GraphIssueSeverity.Error));
    }

    [Fact]
    public async Task ConnectionIntoWrongType_IsTypeMismatch()
    {
        GraphData graph = CharacterGraph("Akagi");
        // The message compiler (node 5) is not a Card
        graph.Connections[0] = Connect(5, 1, nameof(Character.CardId));

        GraphValidationReport report = await Validate(graph);

        GraphIssue issue = Assert.Single(report.Issues, i => i.Code == "connection.type-mismatch");
        Assert.Contains("accepts Card", issue.Message);
    }

    [Fact]
    public async Task UnknownConnector_ListsAvailableInputs()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Connections.Add(Connect(0, 1, "CardIdd"));

        GraphValidationReport report = await Validate(graph);

        GraphIssue issue = Assert.Single(report.Issues, i => i.Code == "connection.unknown-connector");
        Assert.Contains("CardId", issue.Message);
        Assert.Contains("PuppeteerId", issue.Message);
    }

    [Fact]
    public async Task ArraySlots_OutOfRangeAndEmpty_AreErrors()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Nodes[1].ArraySizes["ReflectorIds"] = 2;
        graph.Nodes.Add(Node<SingleReflector>(new { Name = "Reflect" }));
        graph.Connections.Add(Connect(6, 1, nameof(Character.ReflectorIds), 5));

        GraphValidationReport report = await Validate(graph);

        Assert.Contains("connection.array-index-out-of-range", Codes(report, GraphIssueSeverity.Error));
        Assert.Equal(2, report.Issues.Count(i => i.Code == "reference.array-slot-empty"));
    }

    [Fact]
    public async Task UnconnectedRequiredReference_IsWarning()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Connections.RemoveAt(4);

        GraphValidationReport report = await Validate(graph);

        Assert.True(report.IsValid);
        GraphIssue issue = Assert.Single(report.Issues, i => i.Code == "reference.unconnected");
        Assert.Contains("MessageCompilerId", issue.Message);
    }

    [Fact]
    public async Task InvalidPropertyValue_IsError_UnknownProperty_IsWarning()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Nodes[3] = Node(graph.Nodes[3].TypeName, new { MaxSteps = "lots", Bogus = 1 });

        GraphValidationReport report = await Validate(graph);

        Assert.Contains("property.invalid-value", Codes(report, GraphIssueSeverity.Error));
        Assert.Contains("property.unknown", Codes(report, GraphIssueSeverity.Warning));
    }

    [Fact]
    public async Task OrphanNode_IsWarning()
    {
        GraphData graph = CharacterGraph("Akagi");
        graph.Nodes.Add(Node<Card>(new { Name = "Lonely" }));

        GraphValidationReport report = await Validate(graph);

        Assert.Contains("node.orphan", Codes(report, GraphIssueSeverity.Warning));
    }

    [Fact]
    public async Task DuplicateGraphIds_AcrossFiles_AreErrors()
    {
        GraphValidationReport first = await Validate(CharacterGraph("A", "same-id"));
        GraphValidationReport second = await Validate(CharacterGraph("B", "same-id"));

        GraphValidator.CheckDuplicateGraphIds([first, second]);

        Assert.False(first.IsValid);
        Assert.False(second.IsValid);
        Assert.Contains("graph.duplicate-id", Codes(first));
    }

    [Fact]
    public async Task RenamedType_ResolvesThroughAlias()
    {
        GraphData graph = CharacterGraph("Akagi");
        // Node 5 feeds the system processor; use the type's pre-rename name
        graph.Nodes[5] = Node("Akagi.Characters.CharacterBehaviors.MessageCompilers.FilterGroupMemberMessageCompiler, Akagi, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        GraphValidationReport report = await Validate(graph);

        Assert.True(report.IsValid, report.ToText());
        Assert.Contains("node.renamed", Codes(report, GraphIssueSeverity.Info));
    }

    [Fact]
    public async Task InvalidJson_IsReported()
    {
        GraphValidationReport report = await new GraphValidator().ValidateJsonAsync("{ not json", null, "broken.json");

        Assert.Equal("json.invalid", Assert.Single(report.Issues).Code);
    }

    [Fact]
    public async Task UnresolvableReference_IsError()
    {
        using TempDirectory directory = new();
        string path = directory.Write("Group.json", GroupOfReferences("Missing.json", "AlsoMissing.json"));

        GraphValidationReport report = await new GraphValidator().ValidateFileAsync(path);

        Assert.False(report.IsValid);
        Assert.Equal(2, report.Issues.Count(i => i.Code == "reference.unresolved"));
    }

    [Fact]
    public async Task ResolvedReferences_AreValidatedAfterComposition()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor"));
        GraphData broken = CharacterGraph("Health");
        broken.Connections.RemoveAt(0);
        directory.Write("Health.json", broken);
        string path = directory.Write("Group.json", GroupOfReferences("Tutor.json", "Health.json"));

        GraphValidationReport report = await new GraphValidator().ValidateFileAsync(path);

        Assert.True(report.IsValid, report.ToText());
        Assert.Equal(2, report.ReferenceCount);
        Assert.Equal(15, report.ComposedNodeCount);
        GraphIssue issue = Assert.Single(report.Issues, i => i.Code == "reference.unconnected");
        Assert.Contains("Health.json", issue.Node);
        Assert.Contains("CardId", issue.Message);
    }
}
