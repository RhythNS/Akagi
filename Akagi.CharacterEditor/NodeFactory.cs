using Akagi.Bridge.Attributes;
using Akagi.Utils;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections;
using System.Reflection;

namespace Akagi.CharacterEditor;

public static class NodeFactory
{
    public static NodeViewModel CreateNodeFromType(Type nodeType, double x = 0, double y = 0)
    {
        GraphNodeAttribute? nodeAttr = nodeType.GetCustomAttribute<GraphNodeAttribute>();
        if (nodeAttr == null)
        {
            throw new ArgumentException($"Type {nodeType.Name} must have a NodeAttribute");
        }

        object? instance = Activator.CreateInstance(nodeType);
        if (instance == null)
        {
            throw new InvalidOperationException($"Failed to create instance of {nodeType.Name}");
        }

        // Find the root type for color generation
        string baseTypeName = GetRootTypeName(nodeType);

        // Get documentation from NodeDocuAttribute if present
        NodeDocuAttribute? docuAttr = nodeType.GetCustomAttribute<NodeDocuAttribute>();
        string? documentation = docuAttr?.Documentation;

        NodeWrapper wrapper = new(instance, nodeType.Name, baseTypeName);

        NodeViewModel node = new()
        {
            Title = wrapper.NodeTitle,
            Location = new System.Windows.Point(x, y),
            NodeWrapper = wrapper,
            BaseTypeName = baseTypeName,
            Documentation = documentation
        };

        // Find all properties with NodeInput attribute
        PropertyInfo[] properties = nodeType.GetProperties();
        foreach (PropertyInfo property in properties)
        {
            NodeReferenceAttribute? inputAttr = property.GetCustomAttribute<NodeReferenceAttribute>();
            if (inputAttr != null)
            {
                // For inputs, use the root type of the allowed type for color
                string inputTypeName = GetRootTypeName(inputAttr.AllowedType);
                
                // Determine if the property is a collection type
                bool isCollection = IsCollectionType(property.PropertyType);
                
                // Get documentation from NodeDocuAttribute if present
                NodeDocuAttribute? propDocuAttr = property.GetCustomAttribute<NodeDocuAttribute>();
                string? propDocumentation = propDocuAttr?.Documentation;
                
                // Check if this is an array type (fixed-size arrays)
                if (property.PropertyType.IsArray)
                {
                    // Create an ArrayConnectorGroup for fixed-size arrays
                    Type elementType = property.PropertyType.GetElementType()!;
                    string elementTypeName = GetRootTypeName(inputAttr.AllowedType);
                    
                    ArrayConnectorGroup arrayGroup = new(node, property, property.Name, inputAttr.AllowedType, elementTypeName, propDocumentation);
                    node.ArrayGroups.Add(arrayGroup);
                }
                else if (isCollection)
                {
                    // For other collection types (List, IEnumerable, etc.), keep the old behavior
                    node.Input.Add(new ConnectorViewModel
                    {
                        Title = property.Name,
                        ParentNode = node,
                        AllowedType = inputAttr.AllowedType,
                        PropertyInfo = property,
                        ConnectorTypeName = inputTypeName,
                        IsCollection = true,
                        IsInput = true,
                        Documentation = propDocumentation
                    });
                }
                else
                {
                    // Single connector for non-collection types
                    node.Input.Add(new ConnectorViewModel
                    {
                        Title = property.Name,
                        ParentNode = node,
                        AllowedType = inputAttr.AllowedType,
                        PropertyInfo = property,
                        ConnectorTypeName = inputTypeName,
                        IsCollection = false,
                        IsInput = true,
                        Documentation = propDocumentation
                    });
                }
            }
        }

        // For outputs, use the node's own base type for color
        node.Output.Add(new ConnectorViewModel
        {
            Title = "Reference",
            ParentNode = node,
            AllowedType = nodeType,
            PropertyInfo = null,
            ConnectorTypeName = baseTypeName,
            IsOutput = true
        });

        // Add inline properties for basic types
        foreach (PropertyInfo property in properties)
        {
            // Skip properties that are not settable
            if (!property.CanWrite)
            {
                continue;
            }

            // Skip properties with NodeReferenceAttribute (they're already shown as connectors)
            if (property.GetCustomAttribute<NodeReferenceAttribute>() != null)
            {
                continue;
            }

            // Skip properties with NodeIgnoreAttribute (only shown in properties panel)
            if (property.GetCustomAttribute<NodeIgnoreAttribute>() != null)
            {
                continue;
            }

            // Skip properties that are BSON ignored
            if (property.GetCustomAttribute<BsonIgnoreAttribute>() != null)
            {
                continue;
            }

            // Check if it's a collection of basic types
            Type? elementType = GetCollectionElementType(property.PropertyType);
            bool isBasicCollection = elementType != null && IsBasicType(elementType);

            // Only add basic types or collections of basic types for inline editing
            Type propType = property.PropertyType;
            if (propType == typeof(string) || 
                propType == typeof(bool) || 
                propType == typeof(int) || 
                propType == typeof(double) || 
                propType == typeof(float) ||
                propType.IsEnum ||
                isBasicCollection)
            {
                System.Diagnostics.Debug.WriteLine($"Adding inline property: {property.Name} of type {propType.Name} to node {nodeType.Name}");
                NodePropertyViewModel propertyViewModel = new(property, instance)
                {
                    ParentNode = node
                };
                node.InlineProperties.Add(propertyViewModel);
                
                // If this is a "Name" property, set it as the name property and initialize title
                if (property.Name == "Name" && propType == typeof(string))
                {
                    node.NameProperty = propertyViewModel;
                    node.UpdateTitleFromNameProperty();
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"Node {nodeType.Name} created with {node.InlineProperties.Count} inline properties");

        return node;
    }

    private static bool IsCollectionType(Type type)
    {
        // Check if type is an array
        if (type.IsArray)
        {
            return true;
        }

        // Check if type implements IEnumerable (but not string)
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
        {
            return true;
        }

        return false;
    }

    private static string GetRootTypeName(Type type)
    {
        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            Type? baseType = currentType.BaseType;
            if (baseType != null && baseType != typeof(object) && baseType.GetCustomAttribute<GraphNodeAttribute>() != null)
            {
                currentType = baseType;
            }
            else
            {
                break;
            }
        }
        return currentType?.Name ?? type.Name;
    }

    public static IEnumerable<Type> FindAllNodeTypes()
    {
        return TypeUtils.GetTypeWithAttribute<GraphNodeAttribute>();
    }

    public static IEnumerable<NodeViewModel> CreateNodesFromAllTypes()
    {
        IEnumerable<Type> nodeTypes = FindAllNodeTypes();
        int index = 0;

        foreach (Type nodeType in nodeTypes)
        {
            yield return CreateNodeFromType(nodeType, index * 200, 0);
            index++;
        }
    }

    private static Type? GetCollectionElementType(Type type)
    {
        // Check if it's an array
        if (type.IsArray)
        {
            return type.GetElementType();
        }
        
        // Check if it's a generic collection (List<T>, IEnumerable<T>, etc.)
        if (type.IsGenericType)
        {
            Type[] genericArgs = type.GetGenericArguments();
            if (genericArgs.Length == 1)
            {
                Type genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(List<>) ||
                    genericTypeDefinition == typeof(IList<>) ||
                    genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(ICollection<>))
                {
                    return genericArgs[0];
                }
            }
        }
        
        // Check if it implements IEnumerable<T>
        Type? enumerableInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        
        if (enumerableInterface != null)
        {
            return enumerableInterface.GetGenericArguments()[0];
        }
        
        return null;
    }
    
    private static bool IsBasicType(Type type)
    {
        return type == typeof(string) ||
               type == typeof(int) ||
               type == typeof(double) ||
               type == typeof(float) ||
               type == typeof(bool) ||
               type == typeof(long) ||
               type == typeof(short) ||
               type == typeof(byte) ||
               type == typeof(decimal);
    }
}
