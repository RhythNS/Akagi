using Akagi.Bridge.Attributes;
using Akagi.Utils;
using MongoDB.Bson.Serialization.Attributes;
using System.Reflection;
using System.Text.Json;

namespace Akagi.Graphs;

/// <summary>
/// Shared reflection helpers for working with graph node types.
/// </summary>
internal static class GraphNodeUtils
{
    /// <summary>
    /// Resolves a node type name. Accepts assembly qualified names as stored in graph files,
    /// plain full names, and short names (as long as they are unambiguous among graph node types).
    /// </summary>
    public static Type? ResolveType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        Type? type = Type.GetType(typeName);
        if (type != null)
        {
            return type;
        }

        string withoutAssembly = typeName.Split(',')[0].Trim();
        Type[] nodeTypes = GetAllNodeTypes();
        Type[] candidates = [.. nodeTypes.Where(t =>
            t.FullName == withoutAssembly ||
            t.FullName?.Replace('+', '.') == withoutAssembly ||
            t.Name == withoutAssembly)];

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        return candidates.Length == 0 ? FindByAlias(nodeTypes, withoutAssembly) : null;
    }

    /// <summary>
    /// True when <paramref name="typeName"/> only resolves through a <see cref="NodeAliasAttribute"/>.
    /// </summary>
    public static bool IsAliasedName(string typeName, Type resolved)
    {
        string withoutAssembly = typeName.Split(',')[0].Trim();
        return resolved.FullName != withoutAssembly
            && resolved.GetCustomAttributes<NodeAliasAttribute>().Any(a => a.FormerFullName == withoutAssembly);
    }

    private static Type? FindByAlias(Type[] nodeTypes, string fullName)
    {
        Type[] matches = [.. nodeTypes.Where(t => t.GetCustomAttributes<NodeAliasAttribute>().Any(a => a.FormerFullName == fullName))];
        return matches.Length == 1 ? matches[0] : null;
    }

    public static Type[] GetAllNodeTypes()
    {
        return [.. TypeUtils.GetTypeWithAttribute<GraphNodeAttribute>().OrderBy(t => t.FullName, StringComparer.Ordinal)];
    }

    public static bool IsGraphNode(Type type)
    {
        return type.GetCustomAttribute<GraphNodeAttribute>() != null;
    }

    public static bool CanInstantiate(Type type)
    {
        return !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) != null;
    }

    /// <summary>
    /// The type a node provides on its "Reference" output. Graph references export the type of the node they point to.
    /// </summary>
    public static Type GetOutputType(Type nodeType)
    {
        if (typeof(GraphReference).IsAssignableFrom(nodeType) && CanInstantiate(nodeType))
        {
            GraphReference reference = (GraphReference)Activator.CreateInstance(nodeType)!;
            return reference.ExportedType;
        }
        return nodeType;
    }

    public static IEnumerable<PropertyInfo> GetReferenceProperties(Type nodeType)
    {
        return nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<NodeReferenceAttribute>() != null);
    }

    /// <summary>
    /// Properties that are stored inline in the graph file (everything writable that is not a node reference).
    /// </summary>
    public static IEnumerable<PropertyInfo> GetValueProperties(Type nodeType)
    {
        return nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.GetCustomAttribute<NodeReferenceAttribute>() == null)
            .Where(p => p.GetIndexParameters().Length == 0);
    }

    public static bool IsEditorVisible(PropertyInfo property)
    {
        return property.GetCustomAttribute<NodeIgnoreAttribute>() == null
            && property.GetCustomAttribute<BsonIgnoreAttribute>() == null;
    }

    /// <summary>
    /// Converts a raw property value from a graph file to the property type.
    /// </summary>
    public static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null || (value is not JsonElement && targetType.IsInstanceOfType(value)))
        {
            return value;
        }

        string json = value is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(value);
        try
        {
            return JsonSerializer.Deserialize(json, targetType);
        }
        catch (NotSupportedException) when (TryDeserializeThroughConstructor(json, targetType, out object? wrapped))
        {
            return wrapped;
        }
    }

    /// <summary>
    /// Handles wrapper types System.Text.Json cannot create, such as ReadOnlyDictionary or ReadOnlyCollection,
    /// by deserializing into a mutable collection and passing it to the wrapper's constructor.
    /// </summary>
    private static bool TryDeserializeThroughConstructor(string json, Type targetType, out object? result)
    {
        result = null;
        foreach (ConstructorInfo constructor in targetType.GetConstructors())
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            Type parameterType = parameters[0].ParameterType;
            Type concreteType = parameterType;
            if (parameterType.IsInterface && parameterType.IsGenericType)
            {
                Type definition = parameterType.GetGenericTypeDefinition();
                Type[] arguments = parameterType.GetGenericArguments();
                if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                {
                    concreteType = typeof(Dictionary<,>).MakeGenericType(arguments);
                }
                else if (definition == typeof(IList<>) || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>))
                {
                    concreteType = typeof(List<>).MakeGenericType(arguments);
                }
            }
            if (concreteType.IsInterface || concreteType.IsAbstract || concreteType == targetType)
            {
                continue;
            }

            try
            {
                object? inner = JsonSerializer.Deserialize(json, concreteType);
                result = constructor.Invoke([inner]);
                return true;
            }
            catch
            {
                // Try the next constructor
            }
        }
        return false;
    }

    /// <summary>
    /// Applies the inline properties of a node to an instance. Returns a description for every property that failed.
    /// </summary>
    public static List<string> ApplyProperties(object instance, IReadOnlyDictionary<string, object?> properties)
    {
        List<string> errors = [];
        Type nodeType = instance.GetType();

        foreach (KeyValuePair<string, object?> kvp in properties)
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
                property.SetValue(instance, ConvertValue(kvp.Value, property.PropertyType));
            }
            catch (Exception ex)
            {
                errors.Add($"{kvp.Key}: {(ex.InnerException ?? ex).Message}");
            }
        }

        return errors;
    }

    public static string FriendlyTypeName(Type type)
    {
        if (type.IsArray)
        {
            return FriendlyTypeName(type.GetElementType()!) + "[]";
        }
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null)
        {
            return FriendlyTypeName(nullable) + "?";
        }
        if (type.IsGenericType)
        {
            string name = type.Name[..type.Name.IndexOf('`')];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeName))}>";
        }
        return type == typeof(string) ? "string"
            : type == typeof(int) ? "int"
            : type == typeof(bool) ? "bool"
            : type == typeof(double) ? "double"
            : type == typeof(float) ? "float"
            : type == typeof(long) ? "long"
            : type.FullName?.Replace('+', '.') ?? type.Name;
    }
}
