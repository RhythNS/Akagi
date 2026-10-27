using Akagi.Bridge.Attributes;
using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.Interceptors;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Groups.Strategies;
using Akagi.Characters.TriggerPoints;

namespace Akagi.Graphs;

/// <summary>
/// A placeholder node that stands for a node exported by another graph file.
/// References are resolved by <see cref="GraphComposer"/> before a graph is instantiated:
/// the referenced graph is inlined and every connection from this node is rewired to its exported node.
/// </summary>
[GraphNode]
[NodeDocu("Points to another graph file (or an uploaded graph id) and exposes one of its nodes. " +
    "The referenced graph is inlined when this graph is loaded.")]
internal abstract class GraphReference
{
    [NodeDocu("Path to the referenced graph file, relative to the file containing this node.")]
    public string GraphPath { get; set; } = string.Empty;

    [NodeDocu("GraphId of a graph that was uploaded before. Used when GraphPath is empty or cannot be found.")]
    public string GraphId { get; set; } = string.Empty;

    [NodeDocu("Which node to export: a node Name, or #index. Leave empty to pick the single root node of the exported type.")]
    public string ExportNode { get; set; } = string.Empty;

    [NodeDocu("Property overrides applied to the referenced graph: 'NodeName.Property=value' or '#index.Property=value'. " +
        "Values are parsed as JSON when possible, otherwise used as plain strings.")]
    public List<string> Overrides { get; set; } = [];

    public abstract Type ExportedType { get; }
}

[NodeDocu("References a Character defined in another graph file.")]
internal class CharacterGraphReference : GraphReference
{
    public override Type ExportedType => typeof(Character);
}

[NodeDocu("References a Reflector defined in another graph file.")]
internal class ReflectorGraphReference : GraphReference
{
    public override Type ExportedType => typeof(Reflector);
}

[NodeDocu("References a TriggerPoint defined in another graph file.")]
internal class TriggerPointGraphReference : GraphReference
{
    public override Type ExportedType => typeof(TriggerPoint);
}

[NodeDocu("References a MessageCompiler pipeline defined in another graph file.")]
internal class MessageCompilerGraphReference : GraphReference
{
    public override Type ExportedType => typeof(MessageCompiler);
}

[NodeDocu("References a SystemProcessor defined in another graph file.")]
internal class SystemProcessorGraphReference : GraphReference
{
    public override Type ExportedType => typeof(SystemProcessor);
}

[NodeDocu("References an Interceptor defined in another graph file.")]
internal class InterceptorGraphReference : GraphReference
{
    public override Type ExportedType => typeof(Interceptor);
}

[NodeDocu("References a GroupStrategy defined in another graph file.")]
internal class GroupStrategyGraphReference : GraphReference
{
    public override Type ExportedType => typeof(GroupStrategy);
}
