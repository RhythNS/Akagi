namespace Akagi.Bridge.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class NodeReferenceAttribute : Attribute
{
    public Type AllowedType { get; }

    /// <summary>
    /// Marks references that may stay unconnected. Graph validation does not warn about them.
    /// </summary>
    public bool Optional { get; init; }

    public NodeReferenceAttribute(Type allowedType)
    {
        AllowedType = allowedType;
    }
}
