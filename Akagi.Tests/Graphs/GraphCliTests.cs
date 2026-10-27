using Akagi.Graphs;
using Akagi.Tests.Support;
using System.Text.Json;
using static Akagi.Tests.Support.TestGraphs;

namespace Akagi.Tests.Graphs;

public class GraphCliTests
{
    private static async Task<(int ExitCode, string Output, string Error)> Run(params string[] args)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await new GraphCli(output, error).RunAsync(args);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public void OnlyDashedArguments_AreCliInvocations()
    {
        Assert.True(GraphCli.IsCliInvocation(["--validate", "x.json"]));
        Assert.False(GraphCli.IsCliInvocation([]));
        Assert.False(GraphCli.IsCliInvocation(["graph.json"]));
    }

    [Fact]
    public async Task Validate_ValidFile_ExitsZero()
    {
        using TempDirectory directory = new();
        string path = directory.Write("Akagi.json", CharacterGraph("Akagi"));

        (int exitCode, string output, _) = await Run("--validate", path);

        Assert.Equal(GraphCli.ExitOk, exitCode);
        Assert.Contains("VALID", output);
    }

    [Fact]
    public async Task Validate_Directory_WithBrokenFile_ExitsOneAndEmitsJson()
    {
        using TempDirectory directory = new();
        directory.Write("Good.json", CharacterGraph("Good"));
        GraphData broken = CharacterGraph("Broken");
        broken.Connections.Add(Connect(0, 50, "CardId"));
        directory.Write("Broken.json", broken);

        (int exitCode, string output, _) = await Run("--validate", directory.Path, "--json");

        Assert.Equal(GraphCli.ExitFailed, exitCode);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Contains(document.RootElement.EnumerateArray(), r => !r.GetProperty("Valid").GetBoolean());
    }

    [Fact]
    public async Task Validate_Strict_FailsOnWarnings()
    {
        using TempDirectory directory = new();
        GraphData graph = CharacterGraph("Akagi");
        graph.Connections.RemoveAt(4);
        string path = directory.Write("Akagi.json", graph);

        Assert.Equal(GraphCli.ExitOk, (await Run("--validate", path)).ExitCode);
        Assert.Equal(GraphCli.ExitFailed, (await Run("--validate", path, "--strict")).ExitCode);
    }

    [Fact]
    public async Task DumpNodes_WritesCatalogFile()
    {
        using TempDirectory directory = new();
        string path = Path.Combine(directory.Path, "nodes.json");

        (int exitCode, _, _) = await Run("--dump-nodes", path);

        Assert.Equal(GraphCli.ExitOk, exitCode);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Contains(document.RootElement.GetProperty("Nodes").EnumerateArray(), n => n.GetProperty("Name").GetString() == "Character");
    }

    [Fact]
    public async Task DescribeNode_UnknownName_Fails()
    {
        (int exitCode, _, string error) = await Run("--describe-node", "NoSuchNode");

        Assert.Equal(GraphCli.ExitFailed, exitCode);
        Assert.Contains("Character", error);
    }

    [Fact]
    public async Task Compose_WritesFlattenedGraph()
    {
        using TempDirectory directory = new();
        directory.Write("Tutor.json", CharacterGraph("Tutor"));
        directory.Write("Health.json", CharacterGraph("Health"));
        string group = directory.Write("Group.json", GroupOfReferences("Tutor.json", "Health.json"));
        string output = Path.Combine(directory.Path, "out.json");

        (int exitCode, _, string error) = await Run("--compose", group, "-o", output);

        Assert.Equal(GraphCli.ExitOk, exitCode);
        Assert.Empty(error);
        GraphData composed = GraphData.Load(output);
        Assert.Equal(15, composed.Nodes.Count);
        Assert.False(GraphComposer.ContainsReferences(composed));
    }

    [Fact]
    public async Task UnknownCommand_IsUsageError()
    {
        Assert.Equal(GraphCli.ExitUsage, (await Run("--frobnicate")).ExitCode);
        Assert.Equal(GraphCli.ExitUsage, (await Run("--validate")).ExitCode);
    }
}
