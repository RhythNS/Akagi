namespace Akagi.Bridge.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class NodeReferenceAttribute : Attribute
{
    public Type AllowedType { get; }

    public NodeReferenceAttribute(Type allowedType)
    {
        AllowedType = allowedType;
    }
}
