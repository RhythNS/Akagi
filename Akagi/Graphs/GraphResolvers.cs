namespace Akagi.Graphs;

/// <summary>
/// A graph found for a <see cref="GraphReference"/>.
/// </summary>
/// <param name="Graph">The referenced graph data.</param>
/// <param name="Identity">Stable identity used for cycle detection (full path or graph id).</param>
/// <param name="BaseDirectory">Directory used to resolve relative references inside the referenced graph.</param>
internal record ResolvedGraph(GraphData Graph, string Identity, string? BaseDirectory);

internal interface IGraphResolver
{
    public Task<ResolvedGraph?> ResolveAsync(GraphReference reference, string? baseDirectory);
}

/// <summary>
/// Resolves references against graph files on disk. Paths are resolved relative to the referencing graph,
/// graph ids are looked up in the referencing graph's directory and in any additional search directories.
/// </summary>
internal class FileGraphResolver : IGraphResolver
{
    private readonly string[] _searchDirectories;
    private readonly Dictionary<string, Dictionary<string, string>> _graphIdIndex = new(StringComparer.OrdinalIgnoreCase);

    public FileGraphResolver(params string[] searchDirectories)
    {
        _searchDirectories = searchDirectories;
    }

    public Task<ResolvedGraph?> ResolveAsync(GraphReference reference, string? baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(reference.GraphPath))
        {
            foreach (string directory in CandidateDirectories(baseDirectory))
            {
                string path = Path.GetFullPath(Path.Combine(directory, reference.GraphPath));
                if (File.Exists(path))
                {
                    return Task.FromResult<ResolvedGraph?>(LoadFile(path));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(reference.GraphId))
        {
            foreach (string directory in CandidateDirectories(baseDirectory))
            {
                Dictionary<string, string> index = GetIndex(directory);
                if (index.TryGetValue(reference.GraphId, out string? path))
                {
                    return Task.FromResult<ResolvedGraph?>(LoadFile(path));
                }
            }
        }

        return Task.FromResult<ResolvedGraph?>(null);
    }

    private static ResolvedGraph LoadFile(string path)
    {
        GraphData data = GraphData.Load(path);
        return new ResolvedGraph(data, Path.GetFullPath(path), Path.GetDirectoryName(Path.GetFullPath(path)));
    }

    private IEnumerable<string> CandidateDirectories(string? baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(baseDirectory) && !EmbeddedGraphResolver.IsVirtual(baseDirectory))
        {
            yield return baseDirectory;
        }
        foreach (string directory in _searchDirectories)
        {
            yield return directory;
        }
    }

    private Dictionary<string, string> GetIndex(string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        if (_graphIdIndex.TryGetValue(fullDirectory, out Dictionary<string, string>? index))
        {
            return index;
        }

        index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(fullDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(fullDirectory, "*.json"))
            {
                try
                {
                    GraphData data = GraphData.Load(file);
                    index.TryAdd(data.GraphId, file);
                }
                catch
                {
                    // Not a graph file
                }
            }
        }

        _graphIdIndex[fullDirectory] = index;
        return index;
    }
}

/// <summary>
/// Resolves references from graphs that were already uploaded to the database.
/// </summary>
internal class DatabaseGraphResolver : IGraphResolver
{
    private readonly IGraphDefinitionDatabase _database;
    private readonly string _userId;

    public DatabaseGraphResolver(IGraphDefinitionDatabase database, string userId)
    {
        _database = database;
        _userId = userId;
    }

    public async Task<ResolvedGraph?> ResolveAsync(GraphReference reference, string? baseDirectory)
    {
        GraphDefinition? definition = null;

        if (!string.IsNullOrWhiteSpace(reference.GraphId))
        {
            definition = await _database.GetByGraphId(reference.GraphId, _userId);
        }
        if (definition == null && !string.IsNullOrWhiteSpace(reference.GraphPath))
        {
            definition = await _database.GetByFileName(Path.GetFileName(reference.GraphPath), _userId);
        }
        if (definition == null)
        {
            return null;
        }

        return new ResolvedGraph(GraphData.Parse(definition.Json), "id:" + definition.GraphId, null);
    }
}

/// <summary>
/// Tries each resolver in order.
/// </summary>
internal class CompositeGraphResolver : IGraphResolver
{
    private readonly IGraphResolver[] _resolvers;

    public CompositeGraphResolver(params IGraphResolver[] resolvers)
    {
        _resolvers = resolvers;
    }

    public async Task<ResolvedGraph?> ResolveAsync(GraphReference reference, string? baseDirectory)
    {
        foreach (IGraphResolver resolver in _resolvers)
        {
            ResolvedGraph? resolved = await resolver.ResolveAsync(reference, baseDirectory);
            if (resolved != null)
            {
                return resolved;
            }
        }
        return null;
    }
}
