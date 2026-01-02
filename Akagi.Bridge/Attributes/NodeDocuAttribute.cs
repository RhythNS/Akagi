namespace Akagi.Bridge.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class NodeDocuAttribute : Attribute
{
    public string Documentation { get; }

    public NodeDocuAttribute(string documentation)
    {
        Documentation = documentation;
    }
}
