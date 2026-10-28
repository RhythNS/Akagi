namespace Akagi.Graphs;

/// <summary>
/// Resolves references against the subgraphs embedded in a bundle. Embedded graphs live in a virtual directory tree
/// rooted at the bundle, so relative paths between them keep working; graph ids and file names are fallbacks.
/// </summary>
internal class EmbeddedGraphResolver : IGraphResolver
{
    /// <summary>
    /// Marks base directories that point into the bundle instead of the file system.
    /// </summary>
    public const string Prefix = "embedded:";

    private readonly IReadOnlyList<EmbeddedGraph> _embedded;

    public EmbeddedGraphResolver(IReadOnlyList<EmbeddedGraph> embedded)
    {
        _embedded = embedded;
    }

    public static bool IsVirtual(string? baseDirectory) => baseDirectory?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public Task<ResolvedGraph?> ResolveAsync(GraphReference reference, string? baseDirectory)
    {
        EmbeddedGraph? found = null;
        if (!string.IsNullOrWhiteSpace(reference.GraphPath))
        {
            string directory = IsVirtual(baseDirectory) ? baseDirectory![Prefix.Length..] : string.Empty;
            string path = NormalizePath(directory.Length == 0 ? reference.GraphPath : directory + "/" + reference.GraphPath);
            found = _embedded.FirstOrDefault(e => string.Equals(NormalizePath(e.Path), path, StringComparison.OrdinalIgnoreCase))
                ?? _embedded.FirstOrDefault(e => string.Equals(FileName(e.Path), FileName(reference.GraphPath), StringComparison.OrdinalIgnoreCase));
        }
        if (found == null && !string.IsNullOrWhiteSpace(reference.GraphId))
        {
            found = _embedded.FirstOrDefault(e => string.Equals(e.Graph.GraphId, reference.GraphId, StringComparison.OrdinalIgnoreCase));
        }
        if (found == null)
        {
            return Task.FromResult<ResolvedGraph?>(null);
        }

        string normalized = NormalizePath(found.Path);
        int slash = normalized.LastIndexOf('/');
        string folder = slash < 0 ? string.Empty : normalized[..slash];
        return Task.FromResult<ResolvedGraph?>(new ResolvedGraph(found.Graph, Prefix + normalized, Prefix + folder));
    }

    /// <summary>
    /// Collapses "." and ".." segments and unifies separators: "sub/../Shared/./X.json" becomes "Shared/X.json".
    /// Leading ".." segments that leave the bundle root are kept.
    /// </summary>
    public static string NormalizePath(string path)
    {
        List<string> segments = [];
        foreach (string segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static string FileName(string path) => NormalizePath(path).Split('/')[^1];
}

/// <summary>
/// Packs a graph together with every graph it references, directly or through other references, into one file.
/// The root keeps its references; the bundle only adds the referenced graphs so they no longer have to be uploaded
/// separately or in the right order.
/// </summary>
internal static class GraphBundler
{
    public record BundleResult(GraphData Bundle, List<string> Errors)
    {
        public bool Success => Errors.Count == 0;
    }

    public static async Task<BundleResult> BundleAsync(GraphData root, IGraphResolver resolver, string? baseDirectory)
    {
        GraphData bundle = root.Clone();
        List<EmbeddedGraph> embedded = [.. bundle.Embedded ?? []];
        bundle.Embedded = null;
        List<string> errors = [];

        IGraphResolver lookup = embedded.Count > 0 ? new CompositeGraphResolver(new EmbeddedGraphResolver(embedded), resolver) : resolver;
        Dictionary<string, string> pathByIdentity = new(StringComparer.OrdinalIgnoreCase);
        Queue<(GraphData Graph, string? BaseDirectory, string Label)> pending = new();
        pending.Enqueue((bundle, baseDirectory, "root"));
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            (GraphData graph, string? directory, string label) = pending.Dequeue();
            foreach ((int index, GraphReference reference) in References(graph))
            {
                ResolvedGraph? resolved;
                try
                {
                    resolved = await lookup.ResolveAsync(reference, directory);
                }
                catch (Exception ex)
                {
                    errors.Add($"{label}#{index}: {ex.Message}");
                    continue;
                }
                if (resolved == null)
                {
                    errors.Add($"{label}#{index}: could not resolve GraphPath='{reference.GraphPath}' GraphId='{reference.GraphId}'.");
                    continue;
                }
                if (!visited.Add(resolved.Identity))
                {
                    continue;
                }

                string path = BundlePath(resolved, reference, baseDirectory, directory);
                pathByIdentity[resolved.Identity] = path;
                GraphData child = resolved.Graph.Clone();
                child.Embedded = null;
                if (!embedded.Any(e => string.Equals(EmbeddedGraphResolver.NormalizePath(e.Path), path, StringComparison.OrdinalIgnoreCase)))
                {
                    embedded.Add(new EmbeddedGraph { Path = path, Graph = child });
                }
                pending.Enqueue((child, resolved.BaseDirectory, path));
            }
        }

        bundle.Embedded = embedded.Count > 0 ? embedded : null;
        return new BundleResult(bundle, errors);
    }

    /// <summary>
    /// Instantiates the reference nodes of a graph to read their targets.
    /// </summary>
    internal static IEnumerable<(int Index, GraphReference Reference)> References(GraphData graph)
    {
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            Type? type = GraphNodeUtils.ResolveType(graph.Nodes[i].TypeName);
            if (type == null || !typeof(GraphReference).IsAssignableFrom(type) || !GraphNodeUtils.CanInstantiate(type))
            {
                continue;
            }
            GraphReference reference = (GraphReference)Activator.CreateInstance(type)!;
            GraphNodeUtils.ApplyProperties(reference, graph.Nodes[i].Properties);
            yield return (i, reference);
        }
    }

    /// <summary>
    /// Where an embedded graph sits relative to the root, so the root's relative references find it.
    /// </summary>
    private static string BundlePath(ResolvedGraph resolved, GraphReference reference, string? rootDirectory, string? referencingDirectory)
    {
        if (resolved.Identity.StartsWith(EmbeddedGraphResolver.Prefix, StringComparison.Ordinal))
        {
            return resolved.Identity[EmbeddedGraphResolver.Prefix.Length..];
        }
        if (!string.IsNullOrEmpty(rootDirectory) && Path.IsPathRooted(resolved.Identity))
        {
            return EmbeddedGraphResolver.NormalizePath(Path.GetRelativePath(rootDirectory, resolved.Identity));
        }
        // Found in the database: place it where the reference expects it
        string virtualDirectory = EmbeddedGraphResolver.IsVirtual(referencingDirectory) ? referencingDirectory![EmbeddedGraphResolver.Prefix.Length..] : string.Empty;
        string name = string.IsNullOrWhiteSpace(reference.GraphPath) ? resolved.Graph.GraphId + ".json" : reference.GraphPath;
        return EmbeddedGraphResolver.NormalizePath(virtualDirectory.Length == 0 ? name : virtualDirectory + "/" + name);
    }
}
