using Akagi.Bridge.Attributes;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akagi.Graphs;

/// <summary>
/// Machine readable description of every [GraphNode] type: inline properties, reference inputs and output type.
/// Intended for tools and LLMs that author graph files without the editor.
/// </summary>
internal class NodeCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format { get; init; } = "akagi-node-catalog/1";
    public string GraphFileShape { get; init; } =
        "{ GraphId, Nodes: [{ TypeName, Location: {X,Y}, Properties: {name: value}, ArraySizes: {inputName: size} }], " +
        "Connections: [{ SourceNodeIndex, SourceConnectorName: \"\", TargetNodeIndex, TargetConnectorName: inputName, TargetArrayIndex: -1 or slot }] }. " +
        "A connection feeds the source node's output into an input of the target node.";
    public List<NodeTypeInfo> Nodes { get; init; } = [];
    /// <summary>
    /// LLM tools a SystemProcessor can enable by listing their Name in CommandNames.
    /// </summary>
    public IReadOnlyList<CommandCatalog.CommandInfo> Commands { get; init; } = [];

    public class NodeTypeInfo
    {
        public string Name { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string? BaseType { get; init; }
        public bool Abstract { get; init; }
        public string? Documentation { get; init; }
        public string OutputType { get; init; } = string.Empty;
        public List<InputInfo> Inputs { get; init; } = [];
        public List<PropertyInfoModel> Properties { get; init; } = [];
    }

    public class InputInfo
    {
        public string Name { get; init; } = string.Empty;
        public string AcceptsType { get; init; } = string.Empty;
        public bool IsArray { get; init; }
        public bool Optional { get; init; }
        public string? Documentation { get; init; }
        public List<string> CompatibleNodes { get; init; } = [];
    }

    public class PropertyInfoModel
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public object? Default { get; init; }
        public List<string>? EnumValues { get; init; }
        public bool? FlagsEnum { get; init; }
        public string? Documentation { get; init; }
    }

    public static NodeCatalog Build()
    {
        Type[] nodeTypes = GraphNodeUtils.GetAllNodeTypes();
        Type[] concreteTypes = [.. nodeTypes.Where(GraphNodeUtils.CanInstantiate)];

        NodeCatalog catalog = new() { Commands = CommandCatalog.Commands };
        foreach (Type type in nodeTypes)
        {
            catalog.Nodes.Add(Describe(type, concreteTypes));
        }
        return catalog;
    }

    public NodeTypeInfo? Find(string name)
    {
        return Nodes.FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.FullName, name, StringComparison.OrdinalIgnoreCase));
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    public string ToText()
    {
        StringBuilder builder = new();
        foreach (IGrouping<string, NodeTypeInfo> group in Nodes.GroupBy(n => n.Category).OrderBy(g => g.Key))
        {
            builder.AppendLine($"== {group.Key} ==");
            foreach (NodeTypeInfo node in group)
            {
                builder.AppendLine($"{node.Name}{(node.Abstract ? " (abstract)" : string.Empty)} -> {node.OutputType}");
                if (!string.IsNullOrEmpty(node.Documentation))
                {
                    builder.AppendLine($"    {node.Documentation}");
                }
                foreach (InputInfo input in node.Inputs)
                {
                    builder.AppendLine($"    in  {input.Name}: {input.AcceptsType}{(input.IsArray ? "[]" : string.Empty)}{(input.Optional ? " (optional)" : string.Empty)}");
                }
                foreach (PropertyInfoModel property in node.Properties)
                {
                    string defaultText = property.Default == null ? string.Empty : $" = {JsonSerializer.Serialize(property.Default)}";
                    string enumText = property.EnumValues == null ? string.Empty : $" {{{string.Join("|", property.EnumValues)}}}";
                    builder.AppendLine($"    prop {property.Name}: {property.Type}{enumText}{defaultText}");
                }
            }
            builder.AppendLine();
        }

        builder.AppendLine("== LLM commands (SystemProcessor.CommandNames) ==");
        foreach (CommandCatalog.CommandInfo command in Commands)
        {
            string arguments = string.Join(", ", command.Arguments.Select(a => $"{a.Name}:{a.Type}{(a.Required ? string.Empty : "?")}"));
            builder.AppendLine($"{command.Name}({arguments})");
        }
        return builder.ToString();
    }

    private static NodeTypeInfo Describe(Type type, Type[] concreteTypes)
    {
        object? instance = null;
        if (GraphNodeUtils.CanInstantiate(type))
        {
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch
            {
                // Defaults are optional
            }
        }

        Type? baseType = type.BaseType;
        while (baseType != null && baseType != typeof(object) && !GraphNodeUtils.IsGraphNode(baseType))
        {
            baseType = baseType.BaseType;
        }

        List<InputInfo> inputs = [];
        foreach (PropertyInfo property in GraphNodeUtils.GetReferenceProperties(type))
        {
            NodeReferenceAttribute attribute = property.GetCustomAttribute<NodeReferenceAttribute>()!;
            inputs.Add(new InputInfo
            {
                Name = property.Name,
                AcceptsType = attribute.AllowedType.Name,
                IsArray = property.PropertyType.IsArray,
                Optional = attribute.Optional,
                Documentation = property.GetCustomAttribute<NodeDocuAttribute>()?.Documentation,
                CompatibleNodes = [.. concreteTypes
                    .Where(t => attribute.AllowedType.IsAssignableFrom(GraphNodeUtils.GetOutputType(t)))
                    .Select(t => t.Name)]
            });
        }

        List<PropertyInfoModel> properties = [];
        foreach (PropertyInfo property in GraphNodeUtils.GetValueProperties(type).Where(GraphNodeUtils.IsEditorVisible))
        {
            Type propertyType = property.PropertyType;
            properties.Add(new PropertyInfoModel
            {
                Name = property.Name,
                Type = GraphNodeUtils.FriendlyTypeName(propertyType),
                Default = instance != null && IsSimple(propertyType) ? SafeGet(property, instance) : null,
                EnumValues = propertyType.IsEnum ? [.. Enum.GetNames(propertyType)] : null,
                FlagsEnum = propertyType.IsEnum && propertyType.GetCustomAttribute<FlagsAttribute>() != null ? true : null,
                Documentation = property.GetCustomAttribute<NodeDocuAttribute>()?.Documentation
            });
        }

        return new NodeTypeInfo
        {
            Name = type.Name,
            FullName = type.FullName?.Replace('+', '.') ?? type.Name,
            TypeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name,
            Category = RootName(type),
            BaseType = baseType != null && baseType != typeof(object) ? baseType.Name : null,
            Abstract = !GraphNodeUtils.CanInstantiate(type),
            Documentation = type.GetCustomAttribute<NodeDocuAttribute>()?.Documentation,
            OutputType = GraphNodeUtils.GetOutputType(type).Name,
            Inputs = inputs,
            Properties = properties
        };
    }

    private static object? SafeGet(PropertyInfo property, object instance)
    {
        try
        {
            object? value = property.GetValue(instance);
            return value is Enum enumValue ? enumValue.ToString() : value;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSimple(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return true;
        }
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            Type? element = type.IsArray ? type.GetElementType() : type.GetGenericArguments().FirstOrDefault();
            return element != null && (element.IsPrimitive || element == typeof(string));
        }
        return false;
    }

    private static string RootName(Type type)
    {
        Type current = type;
        while (current.BaseType != null && current.BaseType != typeof(object) && GraphNodeUtils.IsGraphNode(current.BaseType))
        {
            current = current.BaseType;
        }
        return current.DeclaringType != null && GraphNodeUtils.IsGraphNode(current.DeclaringType)
            ? current.DeclaringType.Name
            : current.Name;
    }
}
