using Akagi.Bridge.Attributes;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Akagi.Graphs;

internal class GraphLoader
{
    private readonly IGraphInstanceDatabase _graphInstanceDb;
    private readonly IDatabaseFactory _databaseFactory;
    private GraphData? _graphData = null;

    public GraphLoader(IDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
        _graphInstanceDb = databaseFactory.GetDatabase<IGraphInstanceDatabase>();
    }

    public async Task LoadGraphFromStream(Stream stream)
    {
        using StreamReader reader = new(stream, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        _graphData = JsonSerializer.Deserialize<GraphData>(json);
        if (_graphData == null)
        {
            throw new InvalidDataException("Failed to deserialize graph data");
        }
    }

    public async Task<List<Savable>> Create(string userId, string name)
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        bool exists = await _graphInstanceDb.Exists(_graphData!.GraphId, name, userId);
        if (exists)
        {
            throw new InvalidOperationException("Graph instance already exists for the user.");
        }

        List<object> nodeInstances = [];
        List<Savable> allSavables = [];

        CreateInstances(nodeInstances);
        allSavables.AddRange(await SetupNodeReferencesAndProperties(nodeInstances, userId));
        await SaveGraphInstance(allSavables, userId, name);

        return allSavables;
    }

    public async Task<List<Savable>> Update(string userId, string? name = null)
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        GraphInstance[] existingGraphs;
        if (name == null)
        {
            existingGraphs = await _graphInstanceDb.GetGraphs(_graphData.GraphId, userId);
            if (existingGraphs.Length == 0)
            {
                throw new InvalidOperationException("No graph instances exist for the user.");
            }
        }
        else
        {
            GraphInstance? toLoad = await _graphInstanceDb.GetGraph(_graphData.GraphId, name, userId);
            if (toLoad == null)
            {
                throw new InvalidOperationException("Graph instance does not exist for the user with the specified name.");
            }
            existingGraphs = [toLoad];
        }

        List<object> nodeInstances = [];
        Dictionary<int, string> nodeIndexToExistingId = [];
        List<Savable> allSavables = [];

        foreach (GraphInstance existingGraph in existingGraphs)
        {
            await LoadOrCreateInstances(existingGraph, nodeInstances, nodeIndexToExistingId);
            allSavables.AddRange(await SetupNodeReferencesAndProperties(nodeInstances, userId));
            await SaveGraphInstance(allSavables, userId, existingGraph.Name, existingGraph);
        }
        return allSavables;
    }

    private async Task<GraphInstance> SaveGraphInstance(List<Savable> allSavables, string userId, string name, GraphInstance? graph = null)
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        graph ??= new GraphInstance
        {
            GraphId = _graphData.GraphId,
            UserId = userId,
            Name = name
        };

        graph.SavableInfos = [.. allSavables
                    .Where(s => !string.IsNullOrEmpty(s.Id))
                    .Select(s => new GraphInstance.SavableInfo { SavableId = s.Id!, CollectionName = _databaseFactory.GetDatabase(s).CollectionName })];

        await _databaseFactory.TrySave(graph);
        return graph;
    }

    private void CreateInstances(List<object> nodeInstances)
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        foreach (NodeData nodeData in _graphData.Nodes)
        {
            Type? nodeType = Type.GetType(nodeData.TypeName);
            if (nodeType == null)
            {
                throw new InvalidOperationException($"Type '{nodeData.TypeName}' could not be found.");
            }

            object? instance = Activator.CreateInstance(nodeType);
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to create instance of type '{nodeType.FullName}'.");
            }

            nodeInstances.Add(instance);
        }
    }

    private async Task LoadOrCreateInstances(GraphInstance existingGraph, List<object> nodeInstances, Dictionary<int, string> nodeIndexToExistingId)
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        for (int i = 0; i < _graphData.Nodes.Count; i++)
        {
            NodeData nodeData = _graphData.Nodes[i];
            Type? nodeType = Type.GetType(nodeData.TypeName);
            if (nodeType == null)
            {
                throw new InvalidOperationException($"Type '{nodeData.TypeName}' could not be found.");
            }

            if (i < existingGraph.SavableInfos.Length && typeof(Savable).IsAssignableFrom(nodeType))
            {
                IDatabase database = _databaseFactory.GetDatabase(
                    (Savable)Activator.CreateInstance(nodeType)!
                );

                MethodInfo? method = database.GetType().GetMethod("GetDocumentByIdAsync");
                if (method != null)
                {
                    Task task = (Task)method.Invoke(database, [existingGraph.SavableInfos[i].SavableId])!;
                    await task.ConfigureAwait(false);
                    PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
                    object? existingInstance = resultProperty?.GetValue(task);

                    if (existingInstance != null)
                    {
                        nodeInstances.Add(existingInstance);
                        nodeIndexToExistingId[i] = existingGraph.SavableInfos[i].SavableId;
                        continue;
                    }
                }
            }

            object? instance = Activator.CreateInstance(nodeType);
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to create instance of type '{nodeType.FullName}'.");
            }
            nodeInstances.Add(instance);
        }
    }

    private async Task<List<Savable>> SetupNodeReferencesAndProperties(List<object> nodeInstances, string? userId)
    {
        if (_graphData == null)
        {
            throw new InvalidOperationException("Graph data is not loaded.");
        }

        for (int i = 0; i < _graphData.Nodes.Count; i++)
        {
            NodeData nodeData = _graphData.Nodes[i];
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

            foreach (KeyValuePair<string, object?> kvp in nodeData.Properties)
            {
                PropertyInfo? property = nodeType.GetProperty(kvp.Key);
                if (property == null || !property.CanWrite)
                {
                    continue;
                }

                if (property.GetCustomAttribute<NodeReferenceAttribute>() != null)
                {
                    continue;
                }

                try
                {
                    object? value = kvp.Value;

                    if (value is JsonElement jsonElement)
                    {
                        value = JsonSerializer.Deserialize(jsonElement.GetRawText(), property.PropertyType);
                    }

                    property.SetValue(instance, value);
                }
                catch { }
            }
        }

        Dictionary<int, List<(string PropertyName, int TargetNodeIndex, int ArrayIndex)>> nodeConnections = [];

        foreach (ConnectionData connectionData in _graphData.Connections)
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
                        if (sourceInstance is Savable sourceSavable && instanceToId.TryGetValue(sourceInstance, out string? sourceId))
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

internal class GraphData
{
    public string GraphId { get; set; } = Guid.NewGuid().ToString();
    public List<NodeData> Nodes { get; set; } = [];
    public List<ConnectionData> Connections { get; set; } = [];
    public PointData? ViewportLocation { get; set; }
    public double? ViewportZoom { get; set; }
}

internal class NodeData
{
    public string TypeName { get; set; } = "";
    public PointData Location { get; set; } = new();
    public Dictionary<string, object?> Properties { get; set; } = [];
    public Dictionary<string, int> ArraySizes { get; set; } = [];
}

internal class PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}

internal class ConnectionData
{
    public int SourceNodeIndex { get; set; }
    public string SourceConnectorName { get; set; } = "";
    public int TargetNodeIndex { get; set; }
    public string TargetConnectorName { get; set; } = "";
    public int TargetArrayIndex { get; set; } = -1;
}
