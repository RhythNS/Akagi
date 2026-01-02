namespace Akagi.CharacterEditor;

public class NodeWrapper
{
    public object WrappedInstance { get; set; }
    public string NodeTitle { get; set; }
    public string BaseTypeName { get; set; }
    public Dictionary<string, string> InputConnections { get; set; } = [];

    public NodeWrapper(object instance, string nodeTitle, string baseTypeName)
    {
        WrappedInstance = instance;
        NodeTitle = nodeTitle;
        BaseTypeName = baseTypeName;
    }
}
