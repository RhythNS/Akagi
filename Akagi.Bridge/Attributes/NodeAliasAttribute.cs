namespace Akagi.Bridge.Attributes;

/// <summary>
/// Former full type names of a graph node. Graph files that still use an old name keep loading after a rename or move.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class NodeAliasAttribute : Attribute
{
    public string FormerFullName { get; }

    public NodeAliasAttribute(string formerFullName)
    {
        FormerFullName = formerFullName;
    }
}
