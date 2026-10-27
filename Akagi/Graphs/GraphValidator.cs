using Akagi.Bridge.Attributes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akagi.Graphs;

[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum GraphIssueSeverity
{
    Error,
    Warning,
    Info
}

internal class GraphIssue
{
    public GraphIssueSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Node { get; init; }
    public int? NodeIndex { get; init; }

    public override string ToString()
    {
        string location = Node == null ? string.Empty : $" [{Node}]";
        return $"{Severity.ToString().ToUpperInvariant()} {Code}{location}: {Message}";
    }
}

internal class GraphValidationReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string? Source { get; set; }
    public string? GraphId { get; set; }
    public int NodeCount { get; set; }
    public int ConnectionCount { get; set; }
    public int ReferenceCount { get; set; }
    public int? ComposedNodeCount { get; set; }
    public List<GraphIssue> Issues { get; } = [];

    public bool IsValid => !Issues.Any(i => i.Severity == GraphIssueSeverity.Error);
    public int ErrorCount => Issues.Count(i => i.Severity == GraphIssueSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == GraphIssueSeverity.Warning);

    public void Add(GraphIssueSeverity severity, string code, string message, string? node = null, int? nodeIndex = null)
    {
        Issues.Add(new GraphIssue { Severity = severity, Code = code, Message = message, Node = node, NodeIndex = nodeIndex });
    }

    public string ToText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Graph: {Source ?? "(stream)"}");
        builder.AppendLine($"GraphId: {GraphId}");
        builder.Append($"Nodes: {NodeCount}, Connections: {ConnectionCount}");
        if (ReferenceCount > 0)
        {
            builder.Append($", References: {ReferenceCount}, Composed nodes: {ComposedNodeCount?.ToString() ?? "-"}");
        }
        builder.AppendLine();

        foreach (GraphIssue issue in Issues.OrderBy(i => i.Severity))
        {
            builder.AppendLine("  " + issue);
        }

        builder.AppendLine($"Result: {(IsValid ? "VALID" : "INVALID")} ({ErrorCount} errors, {WarningCount} warnings)");
        return builder.ToString();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            Source,
            GraphId,
            Valid = IsValid,
            Errors = ErrorCount,
            Warnings = WarningCount,
            NodeCount,
            ConnectionCount,
            ReferenceCount,
            ComposedNodeCount,
            Issues
        }, JsonOptions);
    }
}

/// <summary>
/// Static checks for graph files: unknown node types, broken properties, dangling or mistyped connections,
/// unconnected references and unresolvable graph references.
/// </summary>
internal class GraphValidator
{
    private readonly IGraphResolver _resolver;

    public GraphValidator(IGraphResolver? resolver = null)
    {
        _resolver = resolver ?? new FileGraphResolver();
    }

    public async Task<GraphValidationReport> ValidateFileAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            GraphValidationReport missing = new() { Source = fullPath };
            missing.Add(GraphIssueSeverity.Error, "file.not-found", "File does not exist.");
            return missing;
        }

        string json = await File.ReadAllTextAsync(fullPath);
        return await ValidateJsonAsync(json, Path.GetDirectoryName(fullPath), fullPath);
    }

    public async Task<GraphValidationReport> ValidateJsonAsync(string json, string? baseDirectory, string? source = null)
    {
        GraphData graph;
        try
        {
            graph = GraphData.Parse(json);
        }
        catch (Exception ex)
        {
            GraphValidationReport broken = new() { Source = source };
            broken.Add(GraphIssueSeverity.Error, "json.invalid", ex.Message);
            return broken;
        }
        return await ValidateAsync(graph, baseDirectory, source);
    }

    public async Task<GraphValidationReport> ValidateAsync(GraphData graph, string? baseDirectory, string? source = null)
    {
        GraphValidationReport report = new()
        {
            Source = source,
            GraphId = graph.GraphId,
            NodeCount = graph.Nodes.Count,
            ConnectionCount = graph.Connections.Count
        };

        if (string.IsNullOrWhiteSpace(graph.GraphId))
        {
            report.Add(GraphIssueSeverity.Error, "graph.no-id", "GraphId is empty. Graph instances are tracked by GraphId.");
        }
        if (graph.Nodes.Count == 0)
        {
            report.Add(GraphIssueSeverity.Warning, "graph.empty", "Graph has no nodes.");
            return report;
        }

        report.ReferenceCount = graph.Nodes.Count(n =>
        {
            Type? type = GraphNodeUtils.ResolveType(n.TypeName);
            return type != null && typeof(GraphReference).IsAssignableFrom(type);
        });

        if (report.ReferenceCount == 0)
        {
            List<string> labels = [.. graph.Nodes.Select((_, i) => $"#{i}")];
            ValidateStructure(graph, labels, report);
            return report;
        }

        CheckReferenceNodesAreUsed(graph, report);

        GraphComposer composer = new(_resolver);
        GraphCompositionResult composition = await composer.ComposeAsync(graph, baseDirectory, source);
        foreach (string error in composition.Errors)
        {
            report.Add(GraphIssueSeverity.Error, "reference.unresolved", error);
        }

        report.ComposedNodeCount = composition.Graph.Nodes.Count;
        ValidateStructure(composition.Graph, composition.Origins, report);
        return report;
    }

    /// <summary>
    /// Graph instances are tracked by GraphId, so two different files with the same id overwrite each other on update.
    /// </summary>
    public static void CheckDuplicateGraphIds(IEnumerable<GraphValidationReport> reports)
    {
        foreach (IGrouping<string, GraphValidationReport> group in reports
            .Where(r => !string.IsNullOrWhiteSpace(r.GraphId))
            .GroupBy(r => r.GraphId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            foreach (GraphValidationReport report in group)
            {
                string others = string.Join(", ", group.Where(r => r != report).Select(r => Path.GetFileName(r.Source ?? "?")));
                report.Add(GraphIssueSeverity.Error, "graph.duplicate-id",
                    $"GraphId {group.Key} is also used by {others}. '/uploadGraph update' would overwrite the other graph's instances; give each file its own GraphId.");
            }
        }
    }

    private static void CheckReferenceNodesAreUsed(GraphData graph, GraphValidationReport report)
    {
        HashSet<int> sources = [.. graph.Connections.Select(c => c.SourceNodeIndex)];
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            Type? type = GraphNodeUtils.ResolveType(graph.Nodes[i].TypeName);
            if (type != null && typeof(GraphReference).IsAssignableFrom(type) && !sources.Contains(i))
            {
                report.Add(GraphIssueSeverity.Warning, "reference.unused", "Graph reference is not connected to anything; its graph is still inlined.", $"#{i} {type.Name}", i);
            }
        }
    }

    private static void ValidateStructure(GraphData graph, IReadOnlyList<string> origins, GraphValidationReport report)
    {
        Type?[] types = new Type?[graph.Nodes.Count];

        string Label(int index)
        {
            NodeData node = graph.Nodes[index];
            string origin = index < origins.Count ? origins[index] : $"#{index}";
            return string.IsNullOrEmpty(node.DisplayName)
                ? $"{origin} {node.ShortTypeName}"
                : $"{origin} {node.ShortTypeName} '{node.DisplayName}'";
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            types[i] = ValidateNode(graph.Nodes[i], Label(i), i, report);
        }

        Dictionary<(int Node, string Property, int Slot), int> slotUsage = [];

        for (int c = 0; c < graph.Connections.Count; c++)
        {
            ConnectionData connection = graph.Connections[c];
            string description = $"connection {connection.SourceNodeIndex} -> {connection.TargetNodeIndex}.{connection.TargetConnectorName}"
                + (connection.TargetArrayIndex >= 0 ? $"[{connection.TargetArrayIndex}]" : string.Empty);

            bool sourceValid = connection.SourceNodeIndex >= 0 && connection.SourceNodeIndex < graph.Nodes.Count;
            bool targetValid = connection.TargetNodeIndex >= 0 && connection.TargetNodeIndex < graph.Nodes.Count;
            if (!sourceValid)
            {
                report.Add(GraphIssueSeverity.Error, "connection.dangling-source", $"{description}: source node index {connection.SourceNodeIndex} does not exist (graph has {graph.Nodes.Count} nodes).");
            }
            if (!targetValid)
            {
                report.Add(GraphIssueSeverity.Error, "connection.dangling-target", $"{description}: target node index {connection.TargetNodeIndex} does not exist (graph has {graph.Nodes.Count} nodes).");
            }
            if (!sourceValid || !targetValid)
            {
                continue;
            }

            string targetLabel = Label(connection.TargetNodeIndex);

            // Record the slot before type checks so a broken source does not also report the input as unconnected
            (int, string, int) slot = (connection.TargetNodeIndex, connection.TargetConnectorName, connection.TargetArrayIndex);
            if (slotUsage.TryGetValue(slot, out int previous))
            {
                report.Add(GraphIssueSeverity.Warning, "connection.duplicate-input",
                    $"{description}: input is already connected (connection #{previous}); the last connection wins.",
                    targetLabel, connection.TargetNodeIndex);
            }
            else
            {
                slotUsage[slot] = c;
            }

            if (connection.SourceNodeIndex == connection.TargetNodeIndex)
            {
                report.Add(GraphIssueSeverity.Warning, "connection.self-loop", $"{description}: node is connected to itself.", targetLabel, connection.TargetNodeIndex);
            }

            Type? sourceType = types[connection.SourceNodeIndex];
            Type? targetType = types[connection.TargetNodeIndex];
            if (sourceType == null || targetType == null)
            {
                continue;
            }

            PropertyInfo? property = targetType.GetProperty(connection.TargetConnectorName);
            NodeReferenceAttribute? referenceAttribute = property?.GetCustomAttribute<NodeReferenceAttribute>();
            if (property == null || referenceAttribute == null)
            {
                string available = string.Join(", ", GraphNodeUtils.GetReferenceProperties(targetType).Select(p => p.Name));
                report.Add(GraphIssueSeverity.Error, "connection.unknown-connector",
                    $"{description}: {targetType.Name} has no reference input '{connection.TargetConnectorName}'. Available: {(available.Length == 0 ? "none" : available)}.",
                    targetLabel, connection.TargetNodeIndex);
                continue;
            }

            Type outputType = GraphNodeUtils.GetOutputType(sourceType);
            if (!referenceAttribute.AllowedType.IsAssignableFrom(outputType))
            {
                report.Add(GraphIssueSeverity.Error, "connection.type-mismatch",
                    $"{description}: {Label(connection.SourceNodeIndex)} provides {outputType.Name} but '{property.Name}' accepts {referenceAttribute.AllowedType.Name}.",
                    targetLabel, connection.TargetNodeIndex);
            }

            if (property.PropertyType.IsArray)
            {
                int size = graph.Nodes[connection.TargetNodeIndex].ArraySizes.GetValueOrDefault(property.Name, 0);
                if (connection.TargetArrayIndex < 0)
                {
                    report.Add(GraphIssueSeverity.Warning, "connection.array-without-index",
                        $"{description}: '{property.Name}' is an array but the connection has no TargetArrayIndex; the loader will replace the whole array.",
                        targetLabel, connection.TargetNodeIndex);
                }
                else if (connection.TargetArrayIndex >= size)
                {
                    report.Add(GraphIssueSeverity.Error, "connection.array-index-out-of-range",
                        $"{description}: array slot {connection.TargetArrayIndex} does not exist, ArraySizes['{property.Name}'] is {size}.",
                        targetLabel, connection.TargetNodeIndex);
                }
            }
            else if (connection.TargetArrayIndex >= 0)
            {
                report.Add(GraphIssueSeverity.Warning, "connection.index-on-non-array",
                    $"{description}: '{property.Name}' is not an array but TargetArrayIndex is {connection.TargetArrayIndex}; the connection is ignored by the loader.",
                    targetLabel, connection.TargetNodeIndex);
            }
        }

        CheckUnconnectedReferences(graph, types, slotUsage, Label, report);
        CheckOrphans(graph, Label, report);
    }

    private static Type? ValidateNode(NodeData node, string label, int index, GraphValidationReport report)
    {
        Type? type = GraphNodeUtils.ResolveType(node.TypeName);
        if (type == null)
        {
            report.Add(GraphIssueSeverity.Error, "node.unknown-type", $"Type '{node.TypeName}' could not be found.", label, index);
            return null;
        }
        if (GraphNodeUtils.IsAliasedName(node.TypeName, type))
        {
            report.Add(GraphIssueSeverity.Info, "node.renamed", $"Type was renamed to {type.FullName}; re-save the graph in the editor to update it.", label, index);
        }
        if (!GraphNodeUtils.IsGraphNode(type))
        {
            report.Add(GraphIssueSeverity.Error, "node.not-graph-node", $"{type.FullName} is not marked with [GraphNode].", label, index);
        }
        if (!GraphNodeUtils.CanInstantiate(type))
        {
            report.Add(GraphIssueSeverity.Error, "node.not-instantiable", $"{type.FullName} is abstract or has no parameterless constructor.", label, index);
            return type;
        }

        foreach (KeyValuePair<string, object?> kvp in node.Properties)
        {
            PropertyInfo? property = type.GetProperty(kvp.Key);
            if (property == null)
            {
                report.Add(GraphIssueSeverity.Warning, "property.unknown", $"{type.Name} has no property '{kvp.Key}'; the value is ignored.", label, index);
                continue;
            }
            if (property.GetCustomAttribute<NodeReferenceAttribute>() != null)
            {
                report.Add(GraphIssueSeverity.Warning, "property.is-reference", $"'{kvp.Key}' is a reference input; stored value is ignored, use a connection instead.", label, index);
                continue;
            }
            if (!property.CanWrite)
            {
                report.Add(GraphIssueSeverity.Warning, "property.read-only", $"'{kvp.Key}' is read only; the value is ignored.", label, index);
                continue;
            }
            try
            {
                GraphNodeUtils.ConvertValue(kvp.Value, property.PropertyType);
            }
            catch (Exception ex)
            {
                report.Add(GraphIssueSeverity.Error, "property.invalid-value",
                    $"'{kvp.Key}' cannot be converted to {GraphNodeUtils.FriendlyTypeName(property.PropertyType)}: {(ex.InnerException ?? ex).Message}", label, index);
            }
        }

        foreach (KeyValuePair<string, int> kvp in node.ArraySizes)
        {
            PropertyInfo? property = type.GetProperty(kvp.Key);
            if (property == null || !property.PropertyType.IsArray || property.GetCustomAttribute<NodeReferenceAttribute>() == null)
            {
                report.Add(GraphIssueSeverity.Warning, "array.unknown", $"ArraySizes entry '{kvp.Key}' does not match an array reference input of {type.Name}.", label, index);
            }
            if (kvp.Value < 0)
            {
                report.Add(GraphIssueSeverity.Error, "array.negative-size", $"ArraySizes['{kvp.Key}'] is negative.", label, index);
            }
        }

        return type;
    }

    private static void CheckUnconnectedReferences(GraphData graph, Type?[] types, Dictionary<(int Node, string Property, int Slot), int> slotUsage,
        Func<int, string> label, GraphValidationReport report)
    {
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            Type? type = types[i];
            if (type == null || !GraphNodeUtils.CanInstantiate(type))
            {
                continue;
            }

            foreach (PropertyInfo property in GraphNodeUtils.GetReferenceProperties(type))
            {
                NodeReferenceAttribute attribute = property.GetCustomAttribute<NodeReferenceAttribute>()!;
                if (property.PropertyType.IsArray)
                {
                    int size = graph.Nodes[i].ArraySizes.GetValueOrDefault(property.Name, 0);
                    bool wholeArrayConnected = slotUsage.ContainsKey((i, property.Name, -1));
                    for (int slot = 0; slot < size && !wholeArrayConnected; slot++)
                    {
                        if (!slotUsage.ContainsKey((i, property.Name, slot)))
                        {
                            report.Add(GraphIssueSeverity.Error, "reference.array-slot-empty",
                                $"'{property.Name}[{slot}]' ({attribute.AllowedType.Name}) is not connected; reduce ArraySizes['{property.Name}'] or connect it.",
                                label(i), i);
                        }
                    }
                }
                else if (!attribute.Optional && !slotUsage.ContainsKey((i, property.Name, -1)))
                {
                    report.Add(GraphIssueSeverity.Warning, "reference.unconnected",
                        $"'{property.Name}' ({attribute.AllowedType.Name}) is not connected.", label(i), i);
                }
            }
        }
    }

    private static void CheckOrphans(GraphData graph, Func<int, string> label, GraphValidationReport report)
    {
        if (graph.Nodes.Count < 2)
        {
            return;
        }

        HashSet<int> connected = [];
        foreach (ConnectionData connection in graph.Connections)
        {
            connected.Add(connection.SourceNodeIndex);
            connected.Add(connection.TargetNodeIndex);
        }

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            if (!connected.Contains(i))
            {
                report.Add(GraphIssueSeverity.Warning, "node.orphan", "Node has no connections.", label(i), i);
            }
        }
    }
}
