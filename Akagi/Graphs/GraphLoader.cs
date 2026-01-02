using System.Reflection;
using System.Text;
using System.Text.Json;
using Akagi.Bridge.Attributes;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Graphs;

internal class GraphLoader
{
    private readonly IDatabaseFactory _databaseFactory;

    public GraphLoader(IDatabaseFactory databaseFactory)
    {
        _databaseFactory = databaseFactory;
    }

    public async Task<List<Savable>> LoadGraphFromStreamForUser(Stream stream, string? userId = null)
    {
        using StreamReader reader = new(stream, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        return await LoadGraphFromJsonForUser(json, userId);
    }

    public async Task<List<Savable>> LoadGraphFromJsonForUser(string json, string? userId = null)
    {
        GraphData? graphData = JsonSerializer.Deserialize<GraphData>(json);
        if (graphData == null)
        {
            throw new InvalidDataException("Failed to deserialize graph data");
        }

        GraphInstance? existingGraph = null;
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(graphData.GraphId))
        {
            IGraphInstanceDatabase graphInstanceDb = _databaseFactory.GetDatabase<IGraphInstanceDatabase>();
            existingGraph = await graphInstanceDb.GetByGraphIdAndUserId(graphData.GraphId, userId);
        }

        List<object> nodeInstances = [];
        Dictionary<int, string> nodeIndexToExistingId = [];

        if (existingGraph != null)
        {
            for (int i = 0; i < graphData.Nodes.Count; i++)
            {
                NodeData nodeData = graphData.Nodes[i];
                Type? nodeType = Type.GetType(nodeData.TypeName);
                if (nodeType == null)
                {
                    throw new InvalidOperationException($"Type '{nodeData.TypeName}' could not be found.");
                }

                if (i < existingGraph.SavableIds.Length && typeof(Savable).IsAssignableFrom(nodeType))
                {
                    IDatabase database = _databaseFactory.GetDatabase(
                        (Savable)Activator.CreateInstance(nodeType)!
                    );

                    MethodInfo? method = database.GetType().GetMethod("GetDocumentByIdAsync");
                    if (method != null)
                    {
                        Task task = (Task)method.Invoke(database, [existingGraph.SavableIds[i]])!;
                        await task.ConfigureAwait(false);
                        PropertyInfo? resultProperty = task.GetType().GetProperty("Result");
                        object? existingInstance = resultProperty?.GetValue(task);

                        if (existingInstance != null)
                        {
                            nodeInstances.Add(existingInstance);
                            nodeIndexToExistingId[i] = existingGraph.SavableIds[i];
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
        else
        {
            foreach (NodeData nodeData in graphData.Nodes)
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

        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(graphData.GraphId))
        {
            IGraphInstanceDatabase graphInstanceDb = _databaseFactory.GetDatabase<IGraphInstanceDatabase>();

            GraphInstance graphInstance;
            if (existingGraph != null)
            {
                graphInstance = existingGraph;
            }
            else
            {
                graphInstance = new GraphInstance
                {
                    GraphId = graphData.GraphId,
                    UserId = userId
                };
            }

            graphInstance.SavableIds = [.. allSavables
                    .Where(s => !string.IsNullOrEmpty(s.Id))
                    .Select(s => s.Id!)];

            await _databaseFactory.TrySave(graphInstance);
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
