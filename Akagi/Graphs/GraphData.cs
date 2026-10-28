using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akagi.Graphs;

internal class GraphData
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public string GraphId { get; set; } = Guid.NewGuid().ToString();
    public List<NodeData> Nodes { get; set; } = [];
    public List<ConnectionData> Connections { get; set; } = [];
    public PointData? ViewportLocation { get; set; }
    public double? ViewportZoom { get; set; }
    /// <summary>
    /// Subgraphs shipped inside this file, so a graph and everything it references can travel as one document.
    /// References resolve against these before looking on disk or in the database.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<EmbeddedGraph>? Embedded { get; set; }

    [JsonIgnore]
    public bool IsBundle => Embedded is { Count: > 0 };

    public static GraphData Parse(string json)
    {
        GraphData? data = JsonSerializer.Deserialize<GraphData>(json);
        return data ?? throw new InvalidDataException("Failed to deserialize graph data");
    }

    public static GraphData Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, WriteOptions);
    }

    /// <summary>
    /// Creates a deep copy by round tripping through JSON. Property values become JsonElements again.
    /// </summary>
    public GraphData Clone()
    {
        return Parse(ToJson());
    }
}

/// <summary>
/// A referenced graph inside a bundle. <see cref="Path"/> is relative to the bundle's root graph and uses '/'.
/// </summary>
internal class EmbeddedGraph
{
    public string Path { get; set; } = string.Empty;
    public GraphData Graph { get; set; } = new();
}

internal class NodeData
{
    public string TypeName { get; set; } = "";
    public PointData Location { get; set; } = new();
    public Dictionary<string, object?> Properties { get; set; } = [];
    public Dictionary<string, int> ArraySizes { get; set; } = [];

    /// <summary>
    /// Value of the "Name" property, if present and a string. Used to address nodes by name.
    /// </summary>
    [JsonIgnore]
    public string? DisplayName
    {
        get
        {
            if (!Properties.TryGetValue("Name", out object? value) || value == null)
            {
                return null;
            }
            if (value is JsonElement element)
            {
                return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
            }
            return value as string;
        }
    }

    [JsonIgnore]
    public string ShortTypeName
    {
        get
        {
            int comma = TypeName.IndexOf(',');
            string fullName = comma >= 0 ? TypeName[..comma] : TypeName;
            int dot = fullName.LastIndexOf('.');
            return dot >= 0 ? fullName[(dot + 1)..] : fullName;
        }
    }
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
