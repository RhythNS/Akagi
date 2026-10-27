using System.Text.Json;

namespace Akagi.Graphs;

internal class GraphCompositionResult
{
    public required GraphData Graph { get; init; }

    /// <summary>
    /// Maps every node index of the input graph to its index in the composed graph.
    /// Reference nodes map to the node they export, unresolved references map to -1.
    /// </summary>
    public required int[] IndexMap { get; init; }

    /// <summary>
    /// Human readable origin of every node in the composed graph, e.g. "#3 > Roleplay_V2.json#1".
    /// </summary>
    public required List<string> Origins { get; init; }

    public List<string> Errors { get; } = [];

    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Inlines <see cref="GraphReference"/> nodes. The result is a plain graph without references that
/// <see cref="GraphLoader"/> can instantiate like any hand written graph.
/// </summary>
internal class GraphComposer
{
    private readonly IGraphResolver _resolver;

    public int MaxDepth { get; init; } = 16;

    public GraphComposer(IGraphResolver resolver)
    {
        _resolver = resolver;
    }

    public static bool ContainsReferences(GraphData graph)
    {
        return graph.Nodes.Any(n => IsReferenceType(GraphNodeUtils.ResolveType(n.TypeName)));
    }

    public async Task<GraphCompositionResult> ComposeAsync(GraphData graph, string? baseDirectory, string? identity = null)
    {
        List<string> errors = [];
        Stack<string> chain = new();
        chain.Push(identity ?? "graph:" + graph.GraphId);

        FlattenResult flattened = await Flatten(graph, baseDirectory, chain, errors, string.Empty);

        GraphCompositionResult result = new()
        {
            Graph = flattened.Graph,
            IndexMap = flattened.IndexMap,
            Origins = flattened.Origins
        };
        result.Errors.AddRange(errors);
        return result;
    }

    /// <summary>
    /// Composes and throws if any reference could not be resolved.
    /// </summary>
    public async Task<GraphData> ComposeOrThrowAsync(GraphData graph, string? baseDirectory, string? identity = null)
    {
        GraphCompositionResult result = await ComposeAsync(graph, baseDirectory, identity);
        if (!result.Success)
        {
            throw new InvalidOperationException("Graph composition failed:" + Environment.NewLine + string.Join(Environment.NewLine, result.Errors));
        }
        return result.Graph;
    }

    private record FlattenResult(GraphData Graph, int[] IndexMap, List<string> Origins);

    private async Task<FlattenResult> Flatten(GraphData graph, string? baseDirectory, Stack<string> chain, List<string> errors, string path)
    {
        GraphData output = new()
        {
            GraphId = graph.GraphId,
            ViewportLocation = graph.ViewportLocation,
            ViewportZoom = graph.ViewportZoom
        };
        List<string> origins = [];
        int[] map = Enumerable.Repeat(-1, graph.Nodes.Count).ToArray();
        HashSet<int> referenceNodes = [];

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            NodeData node = graph.Nodes[i];
            Type? type = GraphNodeUtils.ResolveType(node.TypeName);
            string label = $"{path}#{i}";

            if (!IsReferenceType(type))
            {
                map[i] = output.Nodes.Count;
                output.Nodes.Add(node);
                origins.Add(label);
                continue;
            }

            referenceNodes.Add(i);
            label = $"{label} ({type!.Name})";

            if (!GraphNodeUtils.CanInstantiate(type))
            {
                errors.Add($"{label}: abstract reference type cannot be used directly.");
                continue;
            }

            GraphReference reference = (GraphReference)Activator.CreateInstance(type)!;
            foreach (string propertyError in GraphNodeUtils.ApplyProperties(reference, node.Properties))
            {
                errors.Add($"{label}: invalid property {propertyError}");
            }

            if (string.IsNullOrWhiteSpace(reference.GraphPath) && string.IsNullOrWhiteSpace(reference.GraphId))
            {
                errors.Add($"{label}: neither GraphPath nor GraphId is set.");
                continue;
            }

            ResolvedGraph? resolved;
            try
            {
                resolved = await _resolver.ResolveAsync(reference, baseDirectory);
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: failed to load referenced graph: {ex.Message}");
                continue;
            }

            if (resolved == null)
            {
                errors.Add($"{label}: could not resolve referenced graph (GraphPath='{reference.GraphPath}', GraphId='{reference.GraphId}').");
                continue;
            }
            if (chain.Contains(resolved.Identity))
            {
                errors.Add($"{label}: reference cycle detected: {string.Join(" -> ", chain.Reverse())} -> {resolved.Identity}");
                continue;
            }
            if (chain.Count >= MaxDepth)
            {
                errors.Add($"{label}: maximum reference depth of {MaxDepth} exceeded.");
                continue;
            }

            GraphData child = resolved.Graph.Clone();
            ApplyOverrides(child, reference.Overrides, errors, label);

            string childName = resolved.Identity.StartsWith("id:", StringComparison.Ordinal)
                ? resolved.Identity
                : Path.GetFileName(resolved.Identity);

            chain.Push(resolved.Identity);
            FlattenResult childResult = await Flatten(child, resolved.BaseDirectory, chain, errors, $"{label} > {childName}");
            chain.Pop();

            int exportIndex = ResolveExport(child, childResult, reference, errors, label);
            if (exportIndex < 0)
            {
                continue;
            }

            int offset = output.Nodes.Count;
            foreach (NodeData childNode in childResult.Graph.Nodes)
            {
                childNode.Location = new PointData
                {
                    X = node.Location.X + childNode.Location.X,
                    Y = node.Location.Y + childNode.Location.Y
                };
                output.Nodes.Add(childNode);
            }
            origins.AddRange(childResult.Origins);
            foreach (ConnectionData connection in childResult.Graph.Connections)
            {
                output.Connections.Add(new ConnectionData
                {
                    SourceNodeIndex = connection.SourceNodeIndex + offset,
                    SourceConnectorName = connection.SourceConnectorName,
                    TargetNodeIndex = connection.TargetNodeIndex + offset,
                    TargetConnectorName = connection.TargetConnectorName,
                    TargetArrayIndex = connection.TargetArrayIndex
                });
            }

            map[i] = offset + exportIndex;
        }

        foreach (ConnectionData connection in graph.Connections)
        {
            if (connection.SourceNodeIndex < 0 || connection.SourceNodeIndex >= graph.Nodes.Count ||
                connection.TargetNodeIndex < 0 || connection.TargetNodeIndex >= graph.Nodes.Count)
            {
                errors.Add($"{path}connection {connection.SourceNodeIndex} -> {connection.TargetNodeIndex}.{connection.TargetConnectorName}: node index out of range.");
                continue;
            }
            if (referenceNodes.Contains(connection.TargetNodeIndex))
            {
                errors.Add($"{path}#{connection.TargetNodeIndex}: graph references have no inputs, connection to '{connection.TargetConnectorName}' is ignored.");
                continue;
            }

            int source = map[connection.SourceNodeIndex];
            int target = map[connection.TargetNodeIndex];
            if (source < 0 || target < 0)
            {
                continue;
            }

            output.Connections.Add(new ConnectionData
            {
                SourceNodeIndex = source,
                SourceConnectorName = connection.SourceConnectorName,
                TargetNodeIndex = target,
                TargetConnectorName = connection.TargetConnectorName,
                TargetArrayIndex = connection.TargetArrayIndex
            });
        }

        return new FlattenResult(output, map, origins);
    }

    /// <summary>
    /// Picks the node of the referenced graph that the reference exposes. Returns an index into the flattened child graph.
    /// </summary>
    private static int ResolveExport(GraphData child, FlattenResult flattened, GraphReference reference, List<string> errors, string label)
    {
        Type exportedType = reference.ExportedType;

        if (!string.IsNullOrWhiteSpace(reference.ExportNode))
        {
            int index = FindNode(child, reference.ExportNode, out string? findError);
            if (index >= 0)
            {
                if (!IsExportable(child.Nodes[index], exportedType))
                {
                    errors.Add($"{label}: export node '{reference.ExportNode}' is a {child.Nodes[index].ShortTypeName}, expected {exportedType.Name}.");
                    return -1;
                }
                return flattened.IndexMap[index];
            }

            // Names may also address nodes that come from references nested inside the child graph
            if (!reference.ExportNode.TrimStart().StartsWith('#'))
            {
                int nested = FindNode(flattened.Graph, reference.ExportNode, out _);
                if (nested >= 0 && IsExportable(flattened.Graph.Nodes[nested], exportedType))
                {
                    return nested;
                }
            }

            errors.Add($"{label}: export node '{reference.ExportNode}' {findError}");
            return -1;
        }

        HashSet<int> usedAsSource = [.. child.Connections.Select(c => c.SourceNodeIndex)];
        List<int> candidates = [];
        for (int i = 0; i < child.Nodes.Count; i++)
        {
            if (IsExportable(child.Nodes[i], exportedType))
            {
                candidates.Add(i);
            }
        }

        List<int> roots = [.. candidates.Where(i => !usedAsSource.Contains(i))];
        if (roots.Count == 1)
        {
            return flattened.IndexMap[roots[0]];
        }
        if (roots.Count == 0 && candidates.Count == 1)
        {
            return flattened.IndexMap[candidates[0]];
        }
        if (candidates.Count == 0)
        {
            errors.Add($"{label}: referenced graph contains no {exportedType.Name} node.");
            return -1;
        }

        IEnumerable<int> ambiguous = roots.Count > 0 ? roots : candidates;
        errors.Add($"{label}: referenced graph has several {exportedType.Name} nodes ({string.Join(", ", ambiguous.Select(i => Describe(child, i)))}); set ExportNode.");
        return -1;
    }

    /// <summary>
    /// Finds a node by "#index" or by its Name property.
    /// </summary>
    public static int FindNode(GraphData graph, string selector, out string? error)
    {
        error = null;
        selector = selector.Trim();

        if (selector.StartsWith('#'))
        {
            if (int.TryParse(selector[1..], out int index) && index >= 0 && index < graph.Nodes.Count)
            {
                return index;
            }
            error = "is not a valid node index.";
            return -1;
        }

        List<int> matches = [];
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            if (string.Equals(graph.Nodes[i].DisplayName, selector, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(i);
            }
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }
        error = matches.Count == 0
            ? "does not match any node Name."
            : $"matches {matches.Count} nodes ({string.Join(", ", matches.Select(i => Describe(graph, i)))}); use #index instead.";
        return -1;
    }

    private static void ApplyOverrides(GraphData child, List<string> overrides, List<string> errors, string label)
    {
        foreach (string entry in overrides)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            int equals = entry.IndexOf('=');
            int dot = equals > 0 ? entry.LastIndexOf('.', equals) : -1;
            if (equals < 0 || dot <= 0)
            {
                errors.Add($"{label}: override '{entry}' must look like 'Node.Property=value'.");
                continue;
            }

            string selector = entry[..dot];
            string propertyName = entry[(dot + 1)..equals].Trim();
            string rawValue = entry[(equals + 1)..];

            int index = FindNode(child, selector, out string? findError);
            if (index < 0)
            {
                errors.Add($"{label}: override '{entry}': node '{selector}' {findError}");
                continue;
            }

            NodeData node = child.Nodes[index];
            Type? propertyType = GraphNodeUtils.ResolveType(node.TypeName)?.GetProperty(propertyName)?.PropertyType;
            if (propertyType == null)
            {
                errors.Add($"{label}: override '{entry}': {node.ShortTypeName} has no property '{propertyName}'.");
                continue;
            }

            node.Properties[propertyName] = ParseOverrideValue(rawValue, propertyType);
        }
    }

    private static JsonElement ParseOverrideValue(string rawValue, Type propertyType)
    {
        if (propertyType != typeof(string))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawValue);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall through to a plain string
            }
        }
        else if (rawValue.Length >= 2 && rawValue.StartsWith('"') && rawValue.EndsWith('"'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(rawValue);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Fall through to a plain string
            }
        }

        return JsonSerializer.SerializeToElement(rawValue);
    }

    private static string Describe(GraphData graph, int index)
    {
        NodeData node = graph.Nodes[index];
        return string.IsNullOrEmpty(node.DisplayName) ? $"#{index}" : $"#{index} '{node.DisplayName}'";
    }

    private static bool IsExportable(NodeData node, Type exportedType)
    {
        Type? type = GraphNodeUtils.ResolveType(node.TypeName);
        return type != null && exportedType.IsAssignableFrom(GraphNodeUtils.GetOutputType(type));
    }

    private static bool IsReferenceType(Type? type)
    {
        return type != null && typeof(GraphReference).IsAssignableFrom(type);
    }
}
