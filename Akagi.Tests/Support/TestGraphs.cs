using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Strategies;
using Akagi.Graphs;
using Akagi.Users;
using System.Text.Json;

namespace Akagi.Tests.Support;

/// <summary>
/// Builders for small graph files used across tests.
/// </summary>
internal static class TestGraphs
{
    public static NodeData Node<T>(object? properties = null, Dictionary<string, int>? arraySizes = null)
    {
        return Node(typeof(T).AssemblyQualifiedName!, properties, arraySizes);
    }

    public static NodeData Node(string typeName, object? properties = null, Dictionary<string, int>? arraySizes = null)
    {
        Dictionary<string, object?> values = [];
        if (properties != null)
        {
            JsonElement element = JsonSerializer.SerializeToElement(properties);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }

        return new NodeData
        {
            TypeName = typeName,
            Properties = values,
            ArraySizes = arraySizes ?? []
        };
    }

    public static ConnectionData Connect(int source, int target, string connector, int slot = -1)
    {
        return new ConnectionData
        {
            SourceNodeIndex = source,
            TargetNodeIndex = target,
            TargetConnectorName = connector,
            TargetArrayIndex = slot
        };
    }

    /// <summary>
    /// A minimal complete character: Card, Character, User, SinglePuppeteer, SystemProcessor, DefaultMessageCompiler.
    /// The Character is node 1.
    /// </summary>
    public static GraphData CharacterGraph(string name, string? graphId = null)
    {
        return new GraphData
        {
            GraphId = graphId ?? Guid.NewGuid().ToString(),
            Nodes =
            [
                Node<Card>(new { Name = name + " Card" }),
                Node<Character>(new { Name = name, Description = name + " description" }),
                Node<User>(new { Name = "Placeholder" }),
                Node<SinglePuppeteer>(new { Name = name + "Puppeteer", MaxSteps = 3 }),
                Node<SystemProcessor>(new { Name = name + "Processor", SystemInstruction = "You are " + name }),
                Node<DefaultMessageCompiler>(),
            ],
            Connections =
            [
                Connect(0, 1, nameof(Akagi.Characters.Character.CardId)),
                Connect(2, 1, nameof(Akagi.Characters.Character.UserId)),
                Connect(3, 1, nameof(Akagi.Characters.Character.PuppeteerId)),
                Connect(4, 3, nameof(SinglePuppeteer.SystemProcessorId)),
                Connect(5, 4, nameof(SystemProcessor.MessageCompilerId)),
            ]
        };
    }

    /// <summary>
    /// A group whose two members are graph references. Node 2 is the manager.
    /// </summary>
    public static GraphData GroupOfReferences(string managerPath, string workerPath, string? graphId = null, object? managerReferenceProperties = null)
    {
        return new GraphData
        {
            GraphId = graphId ?? Guid.NewGuid().ToString(),
            Nodes =
            [
                Node<CharacterGroup>(new { Name = "Companion" }, new Dictionary<string, int> { ["MemberCharacterIds"] = 2 }),
                Node<User>(),
                Node<CharacterGraphReference>(managerReferenceProperties ?? new { GraphPath = managerPath }),
                Node<CharacterGraphReference>(new { GraphPath = workerPath }),
                Node<OrchestratorStrategy>(new { MaxRounds = 5 }),
            ],
            Connections =
            [
                Connect(1, 0, nameof(CharacterGroup.UserId)),
                Connect(2, 0, nameof(CharacterGroup.MemberCharacterIds), 0),
                Connect(3, 0, nameof(CharacterGroup.MemberCharacterIds), 1),
                Connect(4, 0, nameof(CharacterGroup.StrategyId)),
                Connect(2, 4, nameof(OrchestratorStrategy.ManagerCharacterId)),
            ]
        };
    }
}

/// <summary>
/// A temporary directory that is deleted on dispose.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "akagi-tests-" + Guid.NewGuid().ToString("N"));

    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public string Write(string fileName, GraphData graph)
    {
        string path = System.IO.Path.Combine(Path, fileName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, graph.ToJson());
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch
        {
            // Best effort
        }
    }
}
