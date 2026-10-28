using Akagi.Bridge.Attributes;
using Akagi.Data;
using Akagi.Users;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Reflection;
using System.Text;

namespace Akagi.Graphs;

internal class GraphLoader
{
    private readonly IGraphInstanceDatabase _graphInstanceDb;
    private readonly IDatabaseFactory _databaseFactory;
    private GraphData? _graphData = null;

    /// <summary>
    /// Used to inline <see cref="GraphReference"/> nodes. Graphs with references fail to load without one.
    /// </summary>
    public IGraphResolver? Resolver { get; set; }

    /// <summary>
    /// Directory relative graph paths are resolved against.
    /// </summary>
    public string? BaseDirectory { get; set; }

    public GraphData? Graph => _graphData;

    public GraphLoader(IDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
        _graphInstanceDb = databaseFactory.GetDatabase<IGraphInstanceDatabase>();
    }

    public async Task LoadGraphFromStream(Stream stream)
    {
        using StreamReader reader = new(stream, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        _graphData = GraphData.Parse(json);
    }

    public void LoadGraph(GraphData graphData)
    {
        _graphData = graphData;
    }

    public async Task<List<Savable>> Create(string userId, string name)
    {
        GraphData graph = await GetComposedGraph();

        ThrowIfCharactersWithoutCard(graph);

        bool exists = await _graphInstanceDb.Exists(graph.GraphId, name, userId);
        if (exists)
        {
            throw new InvalidOperationException("Graph instance already exists for the user.");
        }

        List<object> nodeInstances = CreateInstances(graph);
        List<Savable> allSavables = await SetupNodeReferencesAndProperties(graph, nodeInstances, userId);
        await SaveGraphInstance(graph, nodeInstances, userId, name);

        return allSavables;
    }

    public async Task<List<Savable>> Update(string userId, string? name = null)
    {
        GraphData graph = await GetComposedGraph();
        ThrowIfCharactersWithoutCard(graph);

        GraphInstance[] existingGraphs;
        if (name == null)
        {
            existingGraphs = await _graphInstanceDb.GetGraphs(graph.GraphId, userId);
            if (existingGraphs.Length == 0)
            {
                throw new InvalidOperationException("No graph instances exist for the user.");
            }
        }
        else
        {
            GraphInstance? toLoad = await _graphInstanceDb.GetGraph(graph.GraphId, name, userId);
            if (toLoad == null)
            {
                throw new InvalidOperationException("Graph instance does not exist for the user with the specified name.");
            }
            existingGraphs = [toLoad];
        }

        List<Savable> allSavables = [];

        foreach (GraphInstance existingGraph in existingGraphs)
        {
            List<object> nodeInstances = await LoadOrCreateInstances(graph, existingGraph);
            allSavables.AddRange(await SetupNodeReferencesAndProperties(graph, nodeInstances, userId));
            await SaveGraphInstance(graph, nodeInstances, userId, existingGraph.Name, existingGraph);
        }
        return allSavables;
    }

    /// <summary>
    /// Indexes of Character nodes without a Card connected to CardId. Such a character would get an id that points
    /// to no card, and it fails as soon as it is used, so graphs with one are refused before anything is saved.
    /// </summary>
    public static List<int> CharactersWithoutCard(GraphData graph)
    {
        List<int> indexes = [];
        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            if (GraphNodeUtils.ResolveType(graph.Nodes[i].TypeName) != typeof(Characters.Character))
            {
                continue;
            }
            if (!graph.Connections.Any(c => c.TargetNodeIndex == i && c.TargetConnectorName == nameof(Characters.Character.CardId)))
            {
                indexes.Add(i);
            }
        }
        return indexes;
    }

    public static string NodeName(NodeData node, int index) =>
        node.Properties.TryGetValue("Name", out object? name) && !string.IsNullOrWhiteSpace(name?.ToString()) ? name.ToString()! : $"#{index}";

    private static void ThrowIfCharactersWithoutCard(GraphData graph)
    {
        List<int> missing = CharactersWithoutCard(graph);
        if (missing.Count > 0)
        {
            string names = string.Join(", ", missing.Select(i => NodeName(graph.Nodes[i], i)));
            throw new InvalidOperationException($"Character {names} has no Card connected to CardId. Add a Card node in the editor and connect it; " +
                                                "its picture can be set afterwards with /cardImage.");
        }
    }

    /// <summary>
    /// Returns the loaded graph with all graph references inlined.
    /// </summary>
    public async Task<GraphData> GetComposedGraph()
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        if (!GraphComposer.ContainsReferences(_graphData))
        {
            return _graphData;
        }

        if (Resolver == null && !_graphData.IsBundle)
        {
            throw new InvalidOperationException("Graph contains graph references but no resolver is configured.");
        }

        // A bundle resolves its references from the graphs it carries
        GraphComposer composer = new(Resolver ?? new CompositeGraphResolver());
        return await composer.ComposeOrThrowAsync(_graphData, BaseDirectory);
    }

    private async Task<GraphInstance> SaveGraphInstance(GraphData graphData, List<object> nodeInstances, string userId, string name, GraphInstance? graph = null)
    {
        graph ??= new GraphInstance
        {
            GraphId = graphData.GraphId,
            UserId = userId,
            Name = name
        };

        List<GraphInstance.SavableInfo> infos = [];
        for (int i = 0; i < nodeInstances.Count; i++)
        {
            if (nodeInstances[i] is Savable savable && savable is not User && !string.IsNullOrEmpty(savable.Id))
            {
                infos.Add(new GraphInstance.SavableInfo
                {
                    SavableId = savable.Id,
                    CollectionName = _databaseFactory.GetDatabase(savable).CollectionName,
                    NodeIndex = i
                });
            }
        }
        graph.SavableInfos = [.. infos];

        await _databaseFactory.TrySave(graph);
        return graph;
    }

    private static List<object> CreateInstances(GraphData graphData)
    {
        List<object> nodeInstances = [];
        foreach (NodeData nodeData in graphData.Nodes)
        {
            nodeInstances.Add(CreateInstance(nodeData));
        }
        return nodeInstances;
    }

    private static object CreateInstance(NodeData nodeData)
    {
        Type nodeType = GraphNodeUtils.ResolveType(nodeData.TypeName)
            ?? throw new InvalidOperationException($"Type '{nodeData.TypeName}' could not be found.");

        return Activator.CreateInstance(nodeType)
            ?? throw new InvalidOperationException($"Failed to create instance of type '{nodeType.FullName}'.");
    }

    /// <summary>
    /// Maps node indices to the savable ids stored in an existing graph instance.
    /// Instances saved before node indices were tracked list their savables in node order, skipping non savable nodes.
    /// </summary>
    internal static Dictionary<int, GraphInstance.SavableInfo> MapExistingSavables(GraphData graphData, GraphInstance existingGraph)
    {
        Dictionary<int, GraphInstance.SavableInfo> map = [];
        GraphInstance.SavableInfo[] infos = existingGraph.SavableInfos;

        if (infos.Any(info => info.NodeIndex >= 0))
        {
            foreach (GraphInstance.SavableInfo info in infos.Where(info => info.NodeIndex >= 0))
            {
                map[info.NodeIndex] = info;
            }
            return map;
        }

        int next = 0;
        for (int i = 0; i < graphData.Nodes.Count && next < infos.Length; i++)
        {
            Type? nodeType = GraphNodeUtils.ResolveType(graphData.Nodes[i].TypeName);
            if (nodeType != null && typeof(Savable).IsAssignableFrom(nodeType))
            {
                map[i] = infos[next++];
            }
        }
        return map;
    }

    private async Task<List<object>> LoadOrCreateInstances(GraphData graphData, GraphInstance existingGraph)
    {
        Dictionary<int, GraphInstance.SavableInfo> existing = MapExistingSavables(graphData, existingGraph);
        List<object> nodeInstances = [];

        for (int i = 0; i < graphData.Nodes.Count; i++)
        {
            NodeData nodeData = graphData.Nodes[i];
            Type nodeType = GraphNodeUtils.ResolveType(nodeData.TypeName)
                ?? throw new InvalidOperationException($"Type '{nodeData.TypeName}' could not be found.");

            if (existing.TryGetValue(i, out GraphInstance.SavableInfo? info) && typeof(Savable).IsAssignableFrom(nodeType))
            {
                object? existingInstance = await TryLoadExisting(nodeType, info);
                if (existingInstance != null)
                {
                    nodeInstances.Add(existingInstance);
                    continue;
                }
            }

            nodeInstances.Add(CreateInstance(nodeData));
        }
        return nodeInstances;
    }

    private async Task<object?> TryLoadExisting(Type nodeType, GraphInstance.SavableInfo info)
    {
        IDatabase database = _databaseFactory.GetDatabase((Savable)Activator.CreateInstance(nodeType)!);
        if (!string.Equals(database.CollectionName, info.CollectionName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        MethodInfo? method = database.GetType().GetMethod("GetDocumentByIdAsync");
        if (method == null)
        {
            return null;
        }

        Task task = (Task)method.Invoke(database, [info.SavableId])!;
        await task.ConfigureAwait(false);
        object? loaded = task.GetType().GetProperty("Result")?.GetValue(task);

        // Only reuse documents of the same concrete type, otherwise a changed node type would keep its old behavior.
        return loaded != null && loaded.GetType() == nodeType ? loaded : null;
    }

    private async Task<List<Savable>> SetupNodeReferencesAndProperties(GraphData graphData, List<object> nodeInstances, string? userId)
    {
        for (int i = 0; i < graphData.Nodes.Count; i++)
        {
            NodeData nodeData = graphData.Nodes[i];
            object instance = nodeInstances[i];
            Type nodeType = instance.GetType();

            foreach (KeyValuePair<string, int> kvp in nodeData.ArraySizes)
            {
                PropertyInfo? property = nodeType.GetProperty(kvp.Key);
                if (property != null && property.PropertyType.IsArray)
                {
                    Type? elementType = property.PropertyType.GetElementType();
                    if (elementType != null)
                    {
                        Array array = Array.CreateInstance(elementType, kvp.Value);

                        NodeReferenceAttribute? nodeRefAttr = property.GetCustomAttribute<NodeReferenceAttribute>();
                        BsonRepresentationAttribute? bsonRepAttr = property.GetCustomAttribute<BsonRepresentationAttribute>();

                        if (nodeRefAttr != null && bsonRepAttr != null &&
                            bsonRepAttr.Representation == BsonType.ObjectId &&
                            elementType == typeof(string))
                        {
                            for (int j = 0; j < array.Length; j++)
                            {
                                array.SetValue(ObjectId.GenerateNewId().ToString(), j);
                            }
                        }

                        property.SetValue(instance, array);
                    }
                }
            }

            foreach (PropertyInfo property in nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite)
                {
                    continue;
                }

                NodeReferenceAttribute? nodeRefAttr = property.GetCustomAttribute<NodeReferenceAttribute>();
                if (nodeRefAttr == null)
                {
                    continue;
                }

                BsonRepresentationAttribute? bsonRepAttr = property.GetCustomAttribute<BsonRepresentationAttribute>();
                if (bsonRepAttr != null && bsonRepAttr.Representation == BsonType.ObjectId)
                {
                    try
                    {
                        if (property.PropertyType == typeof(string))
                        {
                            property.SetValue(instance, ObjectId.GenerateNewId().ToString());
                        }
                    }
                    catch { }
                }
            }

            GraphNodeUtils.ApplyProperties(instance, nodeData.Properties);
        }

        Dictionary<int, List<(string PropertyName, int TargetNodeIndex, int ArrayIndex)>> nodeConnections = [];

        foreach (ConnectionData connectionData in graphData.Connections)
        {
            if (!nodeConnections.TryGetValue(connectionData.TargetNodeIndex, out List<(string PropertyName, int TargetNodeIndex, int ArrayIndex)>? value))
            {
                value = [];
                nodeConnections[connectionData.TargetNodeIndex] = value;
            }

            value.Add((
                connectionData.TargetConnectorName,
                connectionData.SourceNodeIndex,
                connectionData.TargetArrayIndex
            ));
        }

        Dictionary<object, string> instanceToId = [];
        List<Savable> allSavables = [];

        foreach (object instance in nodeInstances)
        {
            // User nodes are placeholders for the user the graph is loaded for; they are never persisted.
            if (instance is User && userId != null)
            {
                instanceToId[instance] = userId;
                continue;
            }

            if (instance is Savable savable)
            {
                await _databaseFactory.TrySave(savable);

                if (!string.IsNullOrEmpty(savable.Id))
                {
                    instanceToId[instance] = savable.Id;
                    allSavables.Add(savable);
                }
            }
        }

        for (int i = 0; i < nodeInstances.Count; i++)
        {
            object instance = nodeInstances[i];
            Type nodeType = instance.GetType();

            if (!nodeConnections.TryGetValue(i, out List<(string PropertyName, int TargetNodeIndex, int ArrayIndex)>? connections))
            {
                continue;
            }

            foreach ((string propertyName, int sourceNodeIndex, int arrayIndex) in connections)
            {
                PropertyInfo? property = nodeType.GetProperty(propertyName);
                if (property == null || !property.CanWrite)
                {
                    continue;
                }

                NodeReferenceAttribute? nodeRefAttr = property.GetCustomAttribute<NodeReferenceAttribute>();
                if (nodeRefAttr == null)
                {
                    continue;
                }

                object sourceInstance = nodeInstances[sourceNodeIndex];

                if (arrayIndex >= 0)
                {
                    object? currentArray = property.GetValue(instance);
                    if (currentArray is Array arr && arrayIndex < arr.Length)
                    {
                        if (instanceToId.TryGetValue(sourceInstance, out string? sourceId))
                        {
                            arr.SetValue(sourceId, arrayIndex);
                        }
                        else
                        {
                            arr.SetValue(sourceInstance, arrayIndex);
                        }
                        property.SetValue(instance, arr);
                    }
                }
                else
                {
                    if (property.PropertyType == typeof(string))
                    {
                        if (instanceToId.TryGetValue(sourceInstance, out string? sourceId))
                        {
                            property.SetValue(instance, sourceId);
                        }
                    }
                    else if (property.PropertyType == typeof(string[]))
                    {
                        if (instanceToId.TryGetValue(sourceInstance, out string? sourceId))
                        {
                            string[] ids = [sourceId];
                            property.SetValue(instance, ids);
                        }
                    }
                    else if (property.PropertyType.IsArray)
                    {
                        Type? elementType = property.PropertyType.GetElementType();
                        if (elementType != null && elementType.IsAssignableFrom(sourceInstance.GetType()))
                        {
                            Array existingArray = (Array?)property.GetValue(instance) ?? Array.CreateInstance(elementType, 1);
                            Array newArray = Array.CreateInstance(elementType, existingArray.Length + 1);
                            Array.Copy(existingArray, newArray, existingArray.Length);
                            newArray.SetValue(sourceInstance, existingArray.Length);
                            property.SetValue(instance, newArray);
                        }
                    }
                    else
                    {
                        property.SetValue(instance, sourceInstance);
                    }
                }
            }
        }

        if (userId != null)
        {
            foreach (object instance in nodeInstances)
            {
                if (instance is Savable savable)
                {
                    Type nodeType = instance.GetType();
                    PropertyInfo[] userIdProperties = [.. nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetCustomAttribute<UserIdAttribute>() != null)];

                    foreach (PropertyInfo userIdProperty in userIdProperties)
                    {
                        if (userIdProperty.CanWrite && userIdProperty.PropertyType == typeof(string))
                        {
                            userIdProperty.SetValue(savable, userId);
                        }
                    }
                }
            }
        }

        foreach (Savable savable in allSavables)
        {
            await _databaseFactory.TrySave(savable);
        }

        return allSavables;
    }
}
