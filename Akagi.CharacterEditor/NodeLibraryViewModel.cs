using Akagi.Bridge.Attributes;
using Akagi.CharacterEditor.UndoRedo;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace Akagi.CharacterEditor;

public class NodeTypeViewModel : INotifyPropertyChanged
{
    public string DisplayName { get; set; } = string.Empty;
    public Type NodeType { get; set; } = typeof(object);
    public bool IsAbstract { get; set; }
    public string BaseTypeName { get; set; } = string.Empty;
    public string? Documentation { get; set; }
    public ObservableCollection<NodeTypeViewModel> Children { get; set; } = [];

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class NodeLibraryViewModel : INotifyPropertyChanged
{
    private readonly EditorViewModel _editorViewModel;
    private string _searchText = string.Empty;

    public ObservableCollection<NodeTypeViewModel> NodeTypes { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchText)));
            FilterNodeTypes();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public NodeLibraryViewModel(EditorViewModel editorViewModel)
    {
        _editorViewModel = editorViewModel;
        LoadNodeTypes();
    }

    private void LoadNodeTypes()
    {
        IEnumerable<Type> allTypes = NodeFactory.FindAllNodeTypes();

        // Build a hierarchy based on inheritance
        Dictionary<Type, NodeTypeViewModel> typeViewModels = [];
        Dictionary<Type, Type?> parentTypes = [];
        Dictionary<Type, Type> rootTypes = [];

        // First pass: identify root types for each type
        foreach (Type type in allTypes)
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
            rootTypes[type] = currentType ?? type;
        }

        foreach (Type type in allTypes)
        {
            GraphNodeAttribute? nodeAttr = type.GetCustomAttribute<GraphNodeAttribute>();
            if (nodeAttr == null) continue;

            // Get documentation from NodeDocuAttribute if present
            NodeDocuAttribute? docuAttr = type.GetCustomAttribute<NodeDocuAttribute>();
            string? documentation = docuAttr?.Documentation;

            NodeTypeViewModel viewModel = new()
            {
                DisplayName = type.Name,
                NodeType = type,
                IsAbstract = type.IsAbstract,
                BaseTypeName = rootTypes.TryGetValue(type, out Type? rootType) ? rootType.Name : type.Name,
                Documentation = documentation
            };

            typeViewModels[type] = viewModel;

            // Find the parent type that also has NodeAttribute
            Type? baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                if (baseType.GetCustomAttribute<GraphNodeAttribute>() != null)
                {
                    parentTypes[type] = baseType;
                    break;
                }
                baseType = baseType.BaseType;
            }

            if (baseType == null || baseType == typeof(object))
            {
                parentTypes[type] = null;
            }
        }

        // Build the tree structure
        foreach (KeyValuePair<Type, NodeTypeViewModel> kvp in typeViewModels)
        {
            Type type = kvp.Key;
            NodeTypeViewModel viewModel = kvp.Value;

            if (parentTypes.TryGetValue(type, out Type? parentType) && parentType != null)
            {
                if (typeViewModels.TryGetValue(parentType, out NodeTypeViewModel? parentViewModel))
                {
                    parentViewModel.Children.Add(viewModel);
                }
            }
            else
            {
                // Root level node
                NodeTypes.Add(viewModel);
            }
        }
    }

    private void FilterNodeTypes()
    {
        if (string.IsNullOrWhiteSpace(_searchText))
        {
            // Show all nodes
            SetVisibilityRecursive(NodeTypes, true);
        }
        else
        {
            // Filter based on search text
            foreach (NodeTypeViewModel nodeType in NodeTypes)
            {
                FilterNodeRecursive(nodeType, _searchText.ToLower());
            }
        }
    }

    private static void SetVisibilityRecursive(IEnumerable<NodeTypeViewModel> nodes, bool visible)
    {
        foreach (NodeTypeViewModel node in nodes)
        {
            node.IsVisible = visible;
            if (node.Children.Count > 0)
            {
                SetVisibilityRecursive(node.Children, visible);
            }
        }
    }

    private static bool FilterNodeRecursive(NodeTypeViewModel node, string searchText)
    {
        bool matchesSearch = node.DisplayName.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
                            node.NodeType.Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);

        bool hasVisibleChildren = false;
        if (node.Children.Count > 0)
        {
            foreach (NodeTypeViewModel child in node.Children)
            {
                if (FilterNodeRecursive(child, searchText))
                {
                    hasVisibleChildren = true;
                }
            }
        }

        bool shouldBeVisible = matchesSearch || hasVisibleChildren;
        node.IsVisible = shouldBeVisible;

        if (shouldBeVisible && hasVisibleChildren)
        {
            node.IsExpanded = true;
        }

        return shouldBeVisible;
    }

    public void CreateNode(NodeTypeViewModel nodeTypeViewModel, Point location)
    {
        if (nodeTypeViewModel.IsAbstract)
        {
            return;
        }

        NodeViewModel node = NodeFactory.CreateNodeFromType(nodeTypeViewModel.NodeType, location.X, location.Y);
        
        // Set EditorViewModel for all array groups
        foreach (ArrayConnectorGroup arrayGroup in node.ArrayGroups)
        {
            arrayGroup.EditorViewModel = _editorViewModel;
        }
        
        _editorViewModel.UndoRedoManager.RecordAction(new AddNodeAction(_editorViewModel.Nodes, node));
        _editorViewModel.Nodes.Add(node);
    }

    public List<NodeTypeViewModel> GetFilteredNodeTypesForConnector(ConnectorViewModel connector)
    {
        List<NodeTypeViewModel> filteredTypes = [];

        if (connector.AllowedType == null)
        {
            return filteredTypes;
        }

        // Flatten the hierarchy
        List<NodeTypeViewModel> allTypes = [];
        FlattenNodeTypes(NodeTypes, allTypes);

        // Filter based on connector type
        foreach (NodeTypeViewModel nodeType in allTypes)
        {
            if (nodeType.IsAbstract)
            {
                continue;
            }

            // Check if this node type can connect to the connector
            if (CanConnectToConnector(nodeType.NodeType, connector))
            {
                filteredTypes.Add(nodeType);
            }
        }

        return filteredTypes;
    }

    private static void FlattenNodeTypes(IEnumerable<NodeTypeViewModel> nodes, List<NodeTypeViewModel> result)
    {
        foreach (NodeTypeViewModel node in nodes)
        {
            result.Add(node);
            if (node.Children.Count > 0)
            {
                FlattenNodeTypes(node.Children, result);
            }
        }
    }

    private static bool CanConnectToConnector(Type nodeType, ConnectorViewModel connector)
    {
        return nodeType.IsAssignableTo(connector.AllowedType);
    }

    public List<NodeTypeViewModel> GetAllNodeTypesHierarchical()
    {
        return [.. NodeTypes];
    }
}
