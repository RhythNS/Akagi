using Akagi.Bridge.Attributes;
using Akagi.CharacterEditor.UndoRedo;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace Akagi.CharacterEditor;

public class ConnectorViewModel : INotifyPropertyChanged
{
    private Point _anchor;
    public Point Anchor
    {
        set
        {
            _anchor = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Anchor)));
        }
        get => _anchor;
    }

    private bool _isConnected;
    public bool IsConnected
    {
        set
        {
            _isConnected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        }
        get => _isConnected;
    }

    public string Title { get; set; } = string.Empty;
    public NodeViewModel? ParentNode { get; set; }
    public Type? AllowedType { get; set; }
    public PropertyInfo? PropertyInfo { get; set; }
    public string ConnectorTypeName { get; set; } = string.Empty;
    public bool IsCollection { get; set; }
    public bool IsInput { get; set; }
    public string? Documentation { get; set; }
    
    // Array-specific properties
    public bool IsArrayElement { get; set; }
    public int ArrayIndex { get; set; }
    public ArrayConnectorGroup? ArrayGroup { get; set; }
    
    public bool IsOutput
    {
        get => !IsInput;
        set => IsInput = !value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

// Represents a group of array connectors that can be resized
public class ArrayConnectorGroup : INotifyPropertyChanged
{
    private int _arraySize;
    
    public NodeViewModel ParentNode { get; set; }
    public PropertyInfo PropertyInfo { get; set; }
    public string Title { get; set; }
    public Type ElementType { get; set; }
    public string ConnectorTypeName { get; set; }
    public string? Documentation { get; set; }
    public EditorViewModel? EditorViewModel { get; set; }
    
    public int ArraySize
    {
        get => _arraySize;
        set
        {
            if (_arraySize != value && value >= 0)
            {
                _arraySize = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArraySize)));
            }
        }
    }
    
    public ObservableCollection<ConnectorViewModel> Elements { get; } = [];
    public ICommand IncreaseArraySizeCommand { get; }
    public ICommand DecreaseArraySizeCommand { get; }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public ArrayConnectorGroup(NodeViewModel parentNode, PropertyInfo propertyInfo, string title, Type elementType, string connectorTypeName, string? documentation)
    {
        ParentNode = parentNode;
        PropertyInfo = propertyInfo;
        Title = title;
        ElementType = elementType;
        ConnectorTypeName = connectorTypeName;
        Documentation = documentation;
        
        IncreaseArraySizeCommand = new DelegateCommand<object>(_ => IncreaseSize());
        DecreaseArraySizeCommand = new DelegateCommand<object>(_ => DecreaseSize(), _ => ArraySize > 0);
    }
    
    private void IncreaseSize()
    {
        if (EditorViewModel != null)
        {
            int oldSize = ArraySize;
            int newSize = oldSize + 1;
            ChangeArraySizeAction action = new(this, oldSize, newSize, EditorViewModel);
            EditorViewModel.UndoRedoManager.RecordAction(action);
            action.Execute();
        }
        else
        {
            ArraySize++;
            AddElement();
        }
        (DecreaseArraySizeCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
    }
    
    private void DecreaseSize()
    {
        if (ArraySize > 0)
        {
            if (EditorViewModel != null)
            {
                int oldSize = ArraySize;
                int newSize = oldSize - 1;
                ChangeArraySizeAction action = new(this, oldSize, newSize, EditorViewModel);
                EditorViewModel.UndoRedoManager.RecordAction(action);
                action.Execute();
            }
            else
            {
                ArraySize--;
                RemoveLastElement();
            }
            (DecreaseArraySizeCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
        }
    }
    
    private void AddElement()
    {
        ConnectorViewModel connector = new()
        {
            Title = $"{Title}[{Elements.Count}]",
            ParentNode = ParentNode,
            AllowedType = ElementType,
            PropertyInfo = PropertyInfo,
            ConnectorTypeName = ConnectorTypeName,
            IsCollection = false,
            IsInput = true,
            Documentation = Documentation,
            IsArrayElement = true,
            ArrayIndex = Elements.Count,
            ArrayGroup = this
        };
        
        Elements.Add(connector);
        
        // Find the position to insert - right after the last element of this array group
        int insertIndex = -1;
        for (int i = ParentNode.Input.Count - 1; i >= 0; i--)
        {
            if (ParentNode.Input[i].ArrayGroup == this)
            {
                insertIndex = i + 1;
                break;
            }
        }
        
        // If no elements found, try to find where this array group should be positioned
        // by looking at the ArrayGroups collection order
        if (insertIndex == -1)
        {
            int arrayGroupIndex = ParentNode.ArrayGroups.IndexOf(this);
            
            // Find the position after the previous array group's elements
            for (int i = arrayGroupIndex - 1; i >= 0; i--)
            {
                ArrayConnectorGroup previousGroup = ParentNode.ArrayGroups[i];
                if (previousGroup.Elements.Count > 0)
                {
                    // Find the last element of the previous group
                    for (int j = ParentNode.Input.Count - 1; j >= 0; j--)
                    {
                        if (ParentNode.Input[j].ArrayGroup == previousGroup)
                        {
                            insertIndex = j + 1;
                            break;
                        }
                    }
                    if (insertIndex != -1)
                        break;
                }
            }
            
            // If still not found, find the first non-array connector and insert before it
            if (insertIndex == -1)
            {
                for (int i = 0; i < ParentNode.Input.Count; i++)
                {
                    if (!ParentNode.Input[i].IsArrayElement)
                    {
                        insertIndex = i;
                        break;
                    }
                }
            }
            
            // If still not found, add at the beginning
            if (insertIndex == -1)
            {
                insertIndex = 0;
            }
        }
        
        ParentNode.Input.Insert(insertIndex, connector);
    }
    
    private void RemoveLastElement()
    {
        if (Elements.Count > 0)
        {
            ConnectorViewModel lastElement = Elements[^1];
            Elements.RemoveAt(Elements.Count - 1);
            ParentNode.Input.Remove(lastElement);
        }
    }
    
    public void SetArraySize(int size, EditorViewModel? editorViewModel = null)
    {
        while (ArraySize < size)
        {
            ArraySize++;
            AddElement();
        }
        
        while (ArraySize > size)
        {
            // Remove connections before removing elements
            if (editorViewModel != null && Elements.Count > 0)
            {
                ConnectorViewModel lastElement = Elements[^1];
                List<ConnectionViewModel> connectionsToRemove = [.. editorViewModel.Connections.Where(c => c.Target == lastElement)];
                    
                foreach (ConnectionViewModel? connection in connectionsToRemove)
                {
                    editorViewModel.Connections.Remove(connection);
                    // Update IsConnected status
                    if (!editorViewModel.Connections.Any(c => c.Source == connection.Source || c.Target == connection.Source))
                    {
                        connection.Source.IsConnected = false;
                    }
                    if (!editorViewModel.Connections.Any(c => c.Source == connection.Target || c.Target == connection.Target))
                    {
                        connection.Target.IsConnected = false;
                    }
                }
            }
            
            ArraySize--;
            RemoveLastElement();
        }
        
        (DecreaseArraySizeCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
    }
}

public class NodeViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }
    }

    public ObservableCollection<ConnectorViewModel> Input { get; set; } = [];
    public ObservableCollection<ConnectorViewModel> Output { get; set; } = [];
    public ObservableCollection<NodePropertyViewModel> InlineProperties { get; set; } = [];
    public ObservableCollection<ArrayConnectorGroup> ArrayGroups { get; set; } = [];

    private Point _location;
    private Point _locationBeforeMove;
    private bool _isMoving;

    public Point Location
    {
        set
        {
            if (_location != value)
            {
                _location = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
            }
        }
        get => _location;
    }

    public void BeginMove()
    {
        _locationBeforeMove = _location;
        _isMoving = true;
    }

    public void EndMove(EditorViewModel? editorViewModel)
    {
        if (_isMoving && editorViewModel != null)
        {
            // Only record if there was a meaningful movement (more than 1 pixel in any direction)
            double dx = Math.Abs(_locationBeforeMove.X - _location.X);
            double dy = Math.Abs(_locationBeforeMove.Y - _location.Y);
            
            if (dx > 1 || dy > 1)
            {
                editorViewModel.UndoRedoManager.RecordAction(
                    new MoveNodeAction(this, _locationBeforeMove, _location));
            }
        }
        _isMoving = false;
    }

    public NodeWrapper? NodeWrapper { get; set; }
    public string BaseTypeName { get; set; } = string.Empty;
    public NodePropertyViewModel? NameProperty { get; set; }
    public string? Documentation { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateTitleFromNameProperty()
    {
        string baseTitle = NodeWrapper?.NodeTitle ?? "Node";
        
        if (NameProperty != null && NameProperty.IsString)
        {
            if (string.IsNullOrWhiteSpace(NameProperty.StringValue))
            {
                Title = baseTitle;
            }
            else
            {
                Title = $"{baseTitle} - {NameProperty.StringValue}";
            }
        }
        else
        {
            Title = baseTitle;
        }
    }
}

public class ConnectionViewModel
{
    public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target)
    {
        Source = source;
        Target = target;

        Source.IsConnected = true;
        Target.IsConnected = true;
    }

    public ConnectorViewModel Source { get; }
    public ConnectorViewModel Target { get; }
}

public class EditorViewModel : INotifyPropertyChanged
{
    public PendingConnectionViewModel PendingConnection { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];
    public ICommand DisconnectConnectorCommand { get; }
    public ICommand DeleteConnectionCommand { get; }
    public ICommand PrintDebugInfoCommand { get; }
    public ICommand DeleteNodesCommand { get; }
    public ICommand DuplicateNodeCommand { get; }
    public ICommand CreateNodeCommand { get; }
    public ICommand CopyNodesCommand { get; }
    public ICommand PasteNodesCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public NodeLibraryViewModel NodeLibrary { get; }
    public UndoRedoManager UndoRedoManager { get; }

    private NodifyObservableCollection<NodeViewModel> _selectedNodes = [];
    public NodifyObservableCollection<NodeViewModel> SelectedNodes
    {
        get => _selectedNodes;
        set
        {
            _selectedNodes = value;
            _selectedNodes.WhenAdded(_ => UpdateSelectedNodeProperties());
            _selectedNodes.WhenRemoved(_ => UpdateSelectedNodeProperties());
            _selectedNodes.WhenCleared(_ => UpdateSelectedNodeProperties());
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodes)));
        }
    }

    public ObservableCollection<NodePropertyViewModel> SelectedNodeProperties { get; } = [];

    // Static clipboard for copy/paste across tabs
    private static List<NodeClipboardData>? _clipboard = null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EditorViewModel()
    {
        UndoRedoManager = new UndoRedoManager();
        UndoRedoManager.StacksChanged += (s, e) =>
        {
            (UndoCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
            (RedoCommand as DelegateCommand<object>)?.RaiseCanExecuteChanged();
        };

        DisconnectConnectorCommand = new DelegateCommand<ConnectorViewModel>(connector =>
        {
            ConnectionViewModel? connection = Connections.FirstOrDefault(x => x.Source == connector || x.Target == connector);
            if (connection != null)
            {
                RemoveConnectionWithUndo(connection);
            }
        });

        DeleteConnectionCommand = new DelegateCommand<ConnectionViewModel>(connection =>
        {
            if (connection != null)
            {
                RemoveConnectionWithUndo(connection);
            }
        });

        PrintDebugInfoCommand = new DelegateCommand<object>(_ => PrintDebugInfo());

        DeleteNodesCommand = new DelegateCommand<object>(_ => DeleteSelectedNodes());
        
        DuplicateNodeCommand = new DelegateCommand<NodeViewModel>(node => DuplicateNode(node));
        
        CopyNodesCommand = new DelegateCommand<object>(_ => CopySelectedNodes());
        
        PasteNodesCommand = new DelegateCommand<object>(_ => PasteNodes());
        
        CreateNodeCommand = new DelegateCommand<NodeTypeViewModel>(nodeType => 
        {
            if (nodeType != null && !nodeType.IsAbstract)
            {
                NodeLibrary!.CreateNode(nodeType, new System.Windows.Point(400, 200));
            }
        });

        UndoCommand = new DelegateCommand<object>(_ => UndoRedoManager.Undo(), _ => UndoRedoManager.CanUndo);
        RedoCommand = new DelegateCommand<object>(_ => UndoRedoManager.Redo(), _ => UndoRedoManager.CanRedo);

        PendingConnection = new PendingConnectionViewModel(this);
        NodeLibrary = new NodeLibraryViewModel(this);

        // Subscribe to SelectedNodes changes
        SelectedNodes.WhenAdded(_ => UpdateSelectedNodeProperties());
        SelectedNodes.WhenCleared(_ => UpdateSelectedNodeProperties());
        SelectedNodes.WhenRemoved(_ => UpdateSelectedNodeProperties());
    }

    private void UpdateSelectedNodeProperties()
    {
        SelectedNodeProperties.Clear();

        // Only show properties if exactly one node is selected
        if (SelectedNodes.Count != 1)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeProperties)));
            return;
        }

        NodeViewModel selectedNode = SelectedNodes[0];
        if (selectedNode?.NodeWrapper?.WrappedInstance == null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeProperties)));
            return;
        }

        // First, add all inline properties (these are the basic editable types)
        foreach (NodePropertyViewModel inlineProperty in selectedNode.InlineProperties)
        {
            inlineProperty.SetEditorViewModel(this);
            SelectedNodeProperties.Add(inlineProperty);
        }

        // Then add any other properties that aren't inline (for completeness in the panel)
        // This includes properties with NodeIgnoreAttribute and non-basic types
        PropertyInfo[] allProperties = selectedNode.NodeWrapper.WrappedInstance.GetType().GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        foreach (PropertyInfo property in allProperties)
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

            // Skip if already added as inline property
            if (selectedNode.InlineProperties.Any(ip => ip.Name == property.Name))
            {
                continue;
            }

            // Add remaining properties (includes NodeIgnore properties and complex types)
            NodePropertyViewModel propertyViewModel = new(property, selectedNode.NodeWrapper.WrappedInstance)
            {
                ParentNode = selectedNode
            };
            propertyViewModel.SetEditorViewModel(this);
            SelectedNodeProperties.Add(propertyViewModel);
            
            // If this is a "Name" property that wasn't added inline, track it for title updates
            if (property.Name == "Name" && property.PropertyType == typeof(string) && selectedNode.NameProperty == null)
            {
                selectedNode.NameProperty = propertyViewModel;
            }
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedNodeProperties)));
    }

    private void PrintDebugInfo()
    {
        System.Diagnostics.Debug.WriteLine("=== Debug Info ===");
        System.Diagnostics.Debug.WriteLine($"Total Nodes: {Nodes.Count}");
        foreach (NodeViewModel node in Nodes)
        {
            System.Diagnostics.Debug.WriteLine($"  Node: {node.Title} at {node.Location}");
            System.Diagnostics.Debug.WriteLine($"    Inputs: {node.Input.Count}, Outputs: {node.Output.Count}");
        }

        System.Diagnostics.Debug.WriteLine($"Total Connections: {Connections.Count}");
        foreach (ConnectionViewModel connection in Connections)
        {
            System.Diagnostics.Debug.WriteLine($"  Connection: {connection.Source.Title} -> {connection.Target.Title}");
        }
        System.Diagnostics.Debug.WriteLine("==================");
    }

    public bool Connect(ConnectorViewModel? source, ConnectorViewModel? target)
    {
        if (source == null || target == null || source == target)
        {
            return false;
        }

        if (source.ParentNode != null && source.ParentNode == target.ParentNode)
        {
            return false;
        }

        if (source.IsInput == target.IsInput)
        {
            return false;
        }

        if (source.IsInput)
        {
            // Swap to ensure source is always output and target is always input
            (target, source) = (source, target);
        }

        // Type checking: Ensure output type is compatible with input type
        if (target.AllowedType != null && source.AllowedType != null)
        {
            if (!target.AllowedType.IsAssignableFrom(source.AllowedType))
            {
                System.Diagnostics.Debug.WriteLine($"Type mismatch: Cannot connect {source.AllowedType.Name} to {target.AllowedType.Name}");
                return false;
            }
        }

        CompositeAction compositeAction = new("Create Connection");

        // If the target is not a collection, remove any existing connections to it
        if (!target.IsCollection)
        {
            List<ConnectionViewModel> existingConnections = [.. Connections.Where(c => c.Target == target)];

            foreach (ConnectionViewModel existingConnection in existingConnections)
            {
                compositeAction.AddAction(new RemoveConnectionAction(Connections, existingConnection));
                
                // Update IsConnected status for the source
                int sourceConnectionCount = Connections.Count(c => c.Source == existingConnection.Source || c.Target == existingConnection.Source);
                if (sourceConnectionCount == 1)
                {
                    existingConnection.Source.IsConnected = false;
                }

                Connections.Remove(existingConnection);
            }
        }

        ConnectionViewModel newConnection = new(source, target);
        Connections.Add(newConnection);
        compositeAction.AddAction(new AddConnectionAction(Connections, newConnection));
        
        UndoRedoManager.RecordAction(compositeAction);
        return true;
    }

    private void RemoveConnectionWithUndo(ConnectionViewModel connection)
    {
        UndoRedoManager.RecordAction(new RemoveConnectionAction(Connections, connection));
        Connections.Remove(connection);
        
        // Update IsConnected status after removal
        UpdateConnectorConnectionStatus(connection.Source);
        UpdateConnectorConnectionStatus(connection.Target);
    }

    private void DeleteSelectedNodes()
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        // Create a list to avoid modifying collection during iteration
        List<NodeViewModel> nodesToDelete = [.. SelectedNodes];
        
        DeleteNodesAction deleteAction = new(Nodes, Connections, nodesToDelete);
        UndoRedoManager.RecordAction(deleteAction);
        
        foreach (NodeViewModel node in nodesToDelete)
        {
            // Remove all connections associated with this node
            List<ConnectionViewModel> connectionsToRemove = [.. Connections.Where(c => c.Source.ParentNode == node || c.Target.ParentNode == node)];

            foreach (ConnectionViewModel connection in connectionsToRemove)
            {
                Connections.Remove(connection);
                
                // Update IsConnected status after removal
                UpdateConnectorConnectionStatus(connection.Source);
                UpdateConnectorConnectionStatus(connection.Target);
            }

            // Remove the node
            Nodes.Remove(node);
        }

        SelectedNodes.Clear();
    }

    private void DuplicateNode(NodeViewModel? node)
    {
        if (node?.NodeWrapper?.WrappedInstance == null)
        {
            return;
        }

        // Create a new node of the same type at an offset position
        Type nodeType = node.NodeWrapper.WrappedInstance.GetType();
        System.Windows.Point newLocation = new(
            node.Location.X + 50, 
            node.Location.Y + 50
        );

        NodeViewModel newNode = NodeFactory.CreateNodeFromType(nodeType, newLocation.X, newLocation.Y);

        // Set EditorViewModel for array groups
        foreach (ArrayConnectorGroup arrayGroup in newNode.ArrayGroups)
        {
            arrayGroup.EditorViewModel = this;
        }

        // Copy array sizes
        foreach (ArrayConnectorGroup sourceArrayGroup in node.ArrayGroups)
        {
            ArrayConnectorGroup? targetArrayGroup = newNode.ArrayGroups.FirstOrDefault(ag => ag.PropertyInfo.Name == sourceArrayGroup.PropertyInfo.Name);
            targetArrayGroup?.SetArraySize(sourceArrayGroup.ArraySize);
        }

        // Copy all public settable properties
        PropertyInfo[] properties = nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        
        foreach (PropertyInfo property in properties)
        {
            if (!property.CanWrite || !property.CanRead)
            {
                continue;
            }

            // Skip properties with NodeReferenceAttribute (they're connections)
            if (property.GetCustomAttribute<NodeReferenceAttribute>() != null)
            {
                continue;
            }

            try
            {
                object? value = property.GetValue(node.NodeWrapper.WrappedInstance);
                property.SetValue(newNode.NodeWrapper!.WrappedInstance, value);
                
                // Update the corresponding inline property view model if it exists
                NodePropertyViewModel? inlineProp = newNode.InlineProperties.FirstOrDefault(p => p.Name == property.Name);
                if (inlineProp != null)
                {
                    // Refresh the view model value from the instance
                    if (property.PropertyType == typeof(string))
                    {
                        inlineProp.StringValue = value?.ToString() ?? string.Empty;
                    }
                    else if (property.PropertyType == typeof(bool))
                    {
                        inlineProp.BoolValue = (bool)(value ?? false);
                    }
                    else if (property.PropertyType == typeof(int))
                    {
                        inlineProp.IntValue = (int)(value ?? 0);
                    }
                    else if (property.PropertyType == typeof(double))
                    {
                        inlineProp.DoubleValue = (double)(value ?? 0.0);
                    }
                    else if (property.PropertyType == typeof(float))
                    {
                        inlineProp.FloatValue = (float)(value ?? 0.0f);
                    }
                    else if (property.PropertyType.IsEnum)
                    {
                        if (inlineProp.IsFlagsEnum && inlineProp.FlagsEnumItems != null && value != null)
                        {
                            // Update flags enum checkboxes
                            int currentValue = Convert.ToInt32(value);
                            foreach (FlagsEnumItemViewModel item in inlineProp.FlagsEnumItems)
                            {
                                int flagValue = Convert.ToInt32(item.EnumValue);
                                item.SetCheckedSilent((currentValue & flagValue) == flagValue);
                            }
                        }
                        else
                        {
                            inlineProp.SelectedEnumValue = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy property {property.Name}: {ex.Message}");
            }
        }

        UndoRedoManager.RecordAction(new AddNodeAction(Nodes, newNode));
        Nodes.Add(newNode);
        
        // Select the new node
        SelectedNodes.Clear();
        SelectedNodes.Add(newNode);
    }

    private void CopySelectedNodes()
    {
        if (SelectedNodes.Count == 0)
        {
            return;
        }

        _clipboard = [];

        // Calculate the bounds of selected nodes to maintain relative positions
        double minX = SelectedNodes.Min(n => n.Location.X);
        double minY = SelectedNodes.Min(n => n.Location.Y);

        // Copy each selected node
        foreach (NodeViewModel node in SelectedNodes)
        {
            if (node?.NodeWrapper?.WrappedInstance == null)
            {
                continue;
            }

            NodeClipboardData clipboardData = new()
            {
                TypeName = node.NodeWrapper.WrappedInstance.GetType().AssemblyQualifiedName ?? "",
                Properties = [],
                ArraySizes = [],
                RelativeX = node.Location.X - minX,
                RelativeY = node.Location.Y - minY
            };

            // Copy array sizes
            foreach (ArrayConnectorGroup arrayGroup in node.ArrayGroups)
            {
                clipboardData.ArraySizes[arrayGroup.PropertyInfo.Name] = arrayGroup.ArraySize;
            }

            // Copy all public settable properties
            PropertyInfo[] properties = node.NodeWrapper.WrappedInstance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanWrite || !property.CanRead)
                {
                    continue;
                }

                // Skip properties with NodeReferenceAttribute (they're connections)
                if (property.GetCustomAttribute<NodeReferenceAttribute>() != null)
                {
                    continue;
                }

                try
                {
                    object? value = property.GetValue(node.NodeWrapper.WrappedInstance);
                    if (value != null)
                    {
                        clipboardData.Properties[property.Name] = System.Text.Json.JsonSerializer.Serialize(value);
                    }
                }
                catch { }
            }

            _clipboard.Add(clipboardData);
        }

        // Copy connections between selected nodes
        List<ConnectionClipboardData> connectionData = [];
        foreach (ConnectionViewModel connection in Connections)
        {
            int sourceIndex = SelectedNodes.IndexOf(connection.Source.ParentNode!);
            int targetIndex = SelectedNodes.IndexOf(connection.Target.ParentNode!);

            // Only copy connections where both nodes are selected
            if (sourceIndex >= 0 && targetIndex >= 0)
            {
                connectionData.Add(new ConnectionClipboardData
                {
                    SourceNodeIndex = sourceIndex,
                    SourceConnectorName = connection.Source.PropertyInfo?.Name ?? "", // Empty string for "Reference" output
                    TargetNodeIndex = targetIndex,
                    TargetConnectorName = connection.Target.PropertyInfo?.Name ?? "",
                    TargetArrayIndex = connection.Target.IsArrayElement ? connection.Target.ArrayIndex : -1
                });
            }
        }

        // Store connections in a shared list
        if (_clipboard.Count > 0)
        {
            _clipboard[0].Connections = connectionData;
        }
    }

    private void PasteNodes()
    {
        if (_clipboard == null || _clipboard.Count == 0)
        {
            return;
        }

        CompositeAction compositeAction = new("Paste Nodes");
        List<NodeViewModel> pastedNodes = [];

        // Determine base paste position
        System.Windows.Point basePosition;
        if (SelectedNodes.Count > 0)
        {
            // Paste at an offset from the first selected node
            basePosition = new System.Windows.Point(
                SelectedNodes[0].Location.X + 50,
                SelectedNodes[0].Location.Y + 50
            );
        }
        else
        {
            // Paste at the center or use a default position
            basePosition = new System.Windows.Point(400, 200);
        }

        // Paste each node maintaining relative positions
        foreach (NodeClipboardData clipboardData in _clipboard)
        {
            Type? nodeType = Type.GetType(clipboardData.TypeName);
            if (nodeType == null)
            {
                continue;
            }

            // Create node
            NodeViewModel newNode = NodeFactory.CreateNodeFromType(nodeType, 0, 0);

            // Set EditorViewModel for array groups
            foreach (ArrayConnectorGroup arrayGroup in newNode.ArrayGroups)
            {
                arrayGroup.EditorViewModel = this;
            }

            // Restore array sizes
            foreach (KeyValuePair<string, int> kvp in clipboardData.ArraySizes)
            {
                ArrayConnectorGroup? arrayGroup = newNode.ArrayGroups.FirstOrDefault(ag => ag.PropertyInfo.Name == kvp.Key);
                arrayGroup?.SetArraySize(kvp.Value);
            }

            // Restore properties
            if (newNode.NodeWrapper?.WrappedInstance != null)
            {
                foreach (KeyValuePair<string, string> kvp in clipboardData.Properties)
                {
                    PropertyInfo? property = nodeType.GetProperty(kvp.Key);
                    if (property != null && property.CanWrite)
                    {
                        try
                        {
                            object? value = System.Text.Json.JsonSerializer.Deserialize(kvp.Value, property.PropertyType);
                            property.SetValue(newNode.NodeWrapper.WrappedInstance, value);

                            // Update the view model if it's an inline property
                            NodePropertyViewModel? inlineProp = newNode.InlineProperties.FirstOrDefault(p => p.Name == kvp.Key);
                            if (inlineProp != null)
                            {
                                // Refresh the view model value from the instance
                                if (property.PropertyType == typeof(string))
                                {
                                    inlineProp.StringValue = value?.ToString() ?? string.Empty;
                                }
                                else if (property.PropertyType == typeof(bool))
                                {
                                    inlineProp.BoolValue = (bool)(value ?? false);
                                }
                                else if (property.PropertyType == typeof(int))
                                {
                                    inlineProp.IntValue = (int)(value ?? 0);
                                }
                                else if (property.PropertyType == typeof(double))
                                {
                                    inlineProp.DoubleValue = (double)(value ?? 0.0);
                                }
                                else if (property.PropertyType == typeof(float))
                                {
                                    inlineProp.FloatValue = (float)(value ?? 0.0f);
                                }
                                else if (property.PropertyType.IsEnum)
                                {
                                    if (inlineProp.IsFlagsEnum && inlineProp.FlagsEnumItems != null && value != null)
                                    {
                                        // Update flags enum checkboxes
                                        int currentValue = Convert.ToInt32(value);
                                        foreach (FlagsEnumItemViewModel item in inlineProp.FlagsEnumItems)
                                        {
                                            int flagValue = Convert.ToInt32(item.EnumValue);
                                            item.SetCheckedSilent((currentValue & flagValue) == flagValue);
                                        }
                                    }
                                    else
                                    {
                                        inlineProp.SelectedEnumValue = value;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // Set position maintaining relative offsets
            newNode.Location = new System.Windows.Point(
                basePosition.X + clipboardData.RelativeX,
                basePosition.Y + clipboardData.RelativeY
            );

            // Add the node
            compositeAction.AddAction(new AddNodeAction(Nodes, newNode));
            Nodes.Add(newNode);
            pastedNodes.Add(newNode);
        }

        // Restore connections between pasted nodes
        if (_clipboard.Count > 0 && _clipboard[0].Connections != null)
        {
            foreach (ConnectionClipboardData connectionData in _clipboard[0].Connections!)
            {
                if (connectionData.SourceNodeIndex >= pastedNodes.Count || connectionData.TargetNodeIndex >= pastedNodes.Count)
                {
                    continue;
                }

                NodeViewModel sourceNode = pastedNodes[connectionData.SourceNodeIndex];
                NodeViewModel targetNode = pastedNodes[connectionData.TargetNodeIndex];

                // Find source connector - if name is empty, it's the Reference output
                ConnectorViewModel? sourceConnector;
                if (string.IsNullOrEmpty(connectionData.SourceConnectorName))
                {
                    // It's the Reference output connector
                    sourceConnector = sourceNode.Output.FirstOrDefault(c => c.Title == "Reference");
                }
                else
                {
                    sourceConnector = sourceNode.Output.FirstOrDefault(c => c.PropertyInfo?.Name == connectionData.SourceConnectorName);
                }

                // Find target connector
                ConnectorViewModel? targetConnector;
                if (connectionData.TargetArrayIndex >= 0)
                {
                    // It's an array element connector
                    ArrayConnectorGroup? arrayGroup = targetNode.ArrayGroups
                        .FirstOrDefault(ag => ag.PropertyInfo.Name == connectionData.TargetConnectorName);
                    
                    if (arrayGroup != null && connectionData.TargetArrayIndex < arrayGroup.Elements.Count)
                    {
                        targetConnector = arrayGroup.Elements[connectionData.TargetArrayIndex];
                    }
                    else
                    {
                        targetConnector = null;
                    }
                }
                else
                {
                    // Regular connector
                    targetConnector = targetNode.Input.FirstOrDefault(c => c.PropertyInfo?.Name == connectionData.TargetConnectorName);
                }

                if (sourceConnector != null && targetConnector != null)
                {
                    ConnectionViewModel newConnection = new(sourceConnector, targetConnector);
                    Connections.Add(newConnection);
                    compositeAction.AddAction(new AddConnectionAction(Connections, newConnection));
                }
            }
        }

        UndoRedoManager.RecordAction(compositeAction);

        // Select the pasted nodes
        SelectedNodes.Clear();
        foreach (NodeViewModel node in pastedNodes)
        {
            SelectedNodes.Add(node);
        }
    }

    private void UpdateConnectorConnectionStatus(ConnectorViewModel connector)
    {
        // Check if this connector still has any connections
        int connectionCount = Connections.Count(c => c.Source == connector || c.Target == connector);
        connector.IsConnected = connectionCount > 0;
    }
}

public class CollectionItemViewModel : INotifyPropertyChanged
{
    private string _value = string.Empty;
    private readonly NodePropertyViewModel _parentProperty;
    
    public int Index { get; set; }
    
    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                _parentProperty.UpdateCollectionValue();
            }
        }
    }
    
    public CollectionItemViewModel(int index, string value, NodePropertyViewModel parentProperty)
    {
        Index = index;
        _value = value;
        _parentProperty = parentProperty;
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class FlagsEnumItemViewModel : INotifyPropertyChanged
{
    private bool _isChecked;
    private readonly NodePropertyViewModel _parentProperty;
    
    public object EnumValue { get; }
    public string DisplayName { get; }
    
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                _parentProperty.UpdateFlagsValue();
            }
        }
    }
    
    public void SetCheckedSilent(bool value)
    {
        if (_isChecked != value)
        {
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }
    
    public FlagsEnumItemViewModel(object enumValue, string displayName, bool isChecked, NodePropertyViewModel parentProperty)
    {
        EnumValue = enumValue;
        DisplayName = displayName;
        _isChecked = isChecked;
        _parentProperty = parentProperty;
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class NodePropertyViewModel : INotifyPropertyChanged
{
    private readonly PropertyInfo _propertyInfo;
    private readonly object _instance;
    private EditorViewModel? _editorViewModel;
    private readonly bool _suppressUndoRecording;
    private bool _isUpdatingFlags;
    private bool _isUpdatingCollection;

    public NodePropertyViewModel(PropertyInfo propertyInfo, object instance)
    {
        _propertyInfo = propertyInfo;
        _instance = instance;
        PropertyType = propertyInfo.PropertyType;
        Name = propertyInfo.Name;

        object? value = propertyInfo.GetValue(instance);

        // Check if this is a collection of basic types
        Type? elementType = GetCollectionElementType(PropertyType);
        if (elementType != null && IsBasicType(elementType))
        {
            IsBasicCollection = true;
            CollectionElementType = elementType;
            CollectionItems = [];
            
            _suppressUndoRecording = true;
            
            // Populate collection items
            if (value is System.Collections.IEnumerable enumerable)
            {
                int index = 0;
                foreach (object? item in enumerable)
                {
                    CollectionItems.Add(new CollectionItemViewModel(
                        index++,
                        item?.ToString() ?? string.Empty,
                        this));
                }
            }
            
            _suppressUndoRecording = false;
            
            AddCollectionItemCommand = new DelegateCommand<object>(_ => AddCollectionItem());
            RemoveCollectionItemCommand = new DelegateCommand<int>(index => RemoveCollectionItem(index), _ => CollectionItems.Count > 0);
        }
        else if (PropertyType.IsEnum)
        {
            // Check if enum has Flags attribute
            bool hasFlags = PropertyType.GetCustomAttribute<FlagsAttribute>() != null;
            IsFlagsEnum = hasFlags;
            
            if (hasFlags)
            {
                // Setup for flags enum
                EnumValues = [.. Enum.GetValues(PropertyType).Cast<object>()];
                FlagsEnumItems = [];
                
                _suppressUndoRecording = true;
                
                int currentValue = value != null ? Convert.ToInt32(value) : 0;
                
                foreach (object enumValue in EnumValues)
                {
                    int flagValue = Convert.ToInt32(enumValue);
                    // Skip 0 values and composite flags (values with more than one bit set)
                    if (flagValue != 0 && IsPowerOfTwo(flagValue))
                    {
                        bool isChecked = (currentValue & flagValue) == flagValue;
                        FlagsEnumItems.Add(new FlagsEnumItemViewModel(
                            enumValue, 
                            enumValue.ToString()!, 
                            isChecked, 
                            this));
                    }
                }
                
                _suppressUndoRecording = false;
            }
            else
            {
                // Regular enum
                EnumValues = [.. Enum.GetValues(PropertyType).Cast<object>()];
                _suppressUndoRecording = true;
                SelectedEnumValue = value;
                _suppressUndoRecording = false;
            }
        }
        else if (PropertyType == typeof(bool))
        {
            _suppressUndoRecording = true;
            BoolValue = (bool)(value ?? false);
            _suppressUndoRecording = false;
        }
        else if (PropertyType == typeof(int))
        {
            _suppressUndoRecording = true;
            IntValue = (int)(value ?? 0);
            _suppressUndoRecording = false;
        }
        else if (PropertyType == typeof(double))
        {
            _suppressUndoRecording = true;
            DoubleValue = (double)(value ?? 0.0);
            _suppressUndoRecording = false;
        }
        else if (PropertyType == typeof(float))
        {
            _suppressUndoRecording = true;
            FloatValue = (float)(value ?? 0.0f);
            _suppressUndoRecording = false;
        }
        else
        {
            _suppressUndoRecording = true;
            StringValue = value?.ToString() ?? string.Empty;
            _suppressUndoRecording = false;
        }
    }
    
    // Collection support
    public bool IsBasicCollection { get; set; }
    public Type? CollectionElementType { get; set; }
    public ObservableCollection<CollectionItemViewModel>? CollectionItems { get; set; }
    public ICommand? AddCollectionItemCommand { get; set; }
    public ICommand? RemoveCollectionItemCommand { get; set; }
    
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
    
    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
    
    private void AddCollectionItem()
    {
        if (CollectionItems == null || CollectionElementType == null)
            return;
            
        string defaultValue = CollectionElementType == typeof(string) ? string.Empty : 
                             CollectionElementType == typeof(bool) ? "false" : "0";
        
        CollectionItems.Add(new CollectionItemViewModel(CollectionItems.Count, defaultValue, this));
        UpdateCollectionValue();
        (RemoveCollectionItemCommand as DelegateCommand<int>)?.RaiseCanExecuteChanged();
    }
    
    private void RemoveCollectionItem(int index)
    {
        if (CollectionItems == null || index < 0 || index >= CollectionItems.Count)
            return;
            
        CollectionItems.RemoveAt(index);
        
        // Update indices
        for (int i = index; i < CollectionItems.Count; i++)
        {
            CollectionItems[i].Index = i;
        }
        
        UpdateCollectionValue();
        (RemoveCollectionItemCommand as DelegateCommand<int>)?.RaiseCanExecuteChanged();
    }
    
    public void UpdateCollectionValue()
    {
        if (_isUpdatingCollection || CollectionItems == null || CollectionElementType == null)
            return;
            
        _isUpdatingCollection = true;
        
        try
        {
            object? oldValue = _propertyInfo.GetValue(_instance);
            
            // Create new collection of the appropriate type
            object? newCollection = null;
            
            if (PropertyType.IsArray)
            {
                // Create array
                Array array = Array.CreateInstance(CollectionElementType, CollectionItems.Count);
                for (int i = 0; i < CollectionItems.Count; i++)
                {
                    object? convertedValue = ConvertValue(CollectionItems[i].Value, CollectionElementType);
                    array.SetValue(convertedValue, i);
                }
                newCollection = array;
            }
            else if (PropertyType.IsGenericType && PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            {
                // Create List<T>
                IList list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(CollectionElementType))!;
                foreach (CollectionItemViewModel item in CollectionItems)
                {
                    object? convertedValue = ConvertValue(item.Value, CollectionElementType);
                    list.Add(convertedValue);
                }
                newCollection = list;
            }
            
            if (newCollection != null)
            {
                _propertyInfo.SetValue(_instance, newCollection);
                
                if (_editorViewModel != null && !_suppressUndoRecording)
                {
                    _editorViewModel.UndoRedoManager.RecordAction(
                        new ChangePropertyAction(_propertyInfo, _instance, oldValue, newCollection, this));
                }
            }
        }
        finally
        {
            _isUpdatingCollection = false;
        }
    }
    
    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string))
                return value;
            else if (targetType == typeof(int))
                return int.Parse(value);
            else if (targetType == typeof(double))
                return double.Parse(value);
            else if (targetType == typeof(float))
                return float.Parse(value);
            else if (targetType == typeof(bool))
                return bool.Parse(value);
            else if (targetType == typeof(long))
                return long.Parse(value);
            else if (targetType == typeof(short))
                return short.Parse(value);
            else if (targetType == typeof(byte))
                return byte.Parse(value);
            else if (targetType == typeof(decimal))
                return decimal.Parse(value);
            else
                return Convert.ChangeType(value, targetType);
        }
        catch
        {
            // Return default value if conversion fails
            if (targetType == typeof(string))
                return string.Empty;
            else
                return Activator.CreateInstance(targetType);
        }
    }

    public string Name { get; set; }
    public Type PropertyType { get; set; }
    public NodeViewModel? ParentNode { get; set; }

    public void SetEditorViewModel(EditorViewModel editorViewModel)
    {
        _editorViewModel = editorViewModel;
    }

    // String value
    private string _stringValue = string.Empty;
    public string StringValue
    {
        get => _stringValue;
        set
        {
            if (_stringValue != value)
            {
                object? oldValue = _propertyInfo.GetValue(_instance);
                _stringValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StringValue)));
                UpdatePropertyValue(value);
                
                if (_editorViewModel != null && !_suppressUndoRecording)
                {
                    _editorViewModel.UndoRedoManager.RecordAction(
                        new ChangePropertyAction(_propertyInfo, _instance, oldValue, value, this));
                }
                
                // Update node title if this is the Name property
                if (Name == "Name" && ParentNode != null)
                {
                    ParentNode.UpdateTitleFromNameProperty();
                }
            }
        }
    }

    // Bool value
    private bool _boolValue;
    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            if (_boolValue != value)
            {
                object? oldValue = _propertyInfo.GetValue(_instance);
                _boolValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoolValue)));
                _propertyInfo.SetValue(_instance, value);
                
                if (_editorViewModel != null && !_suppressUndoRecording)
                {
                    _editorViewModel.UndoRedoManager.RecordAction(
                        new ChangePropertyAction(_propertyInfo, _instance, oldValue, value, this));
                }
            }
        }
    }

    // Int value
    private int _intValue;
    public int IntValue
    {
        get => _intValue;
        set
        {
            if (_intValue != value)
            {
                object? oldValue = _propertyInfo.GetValue(_instance);
                _intValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IntValue)));
                _propertyInfo.SetValue(_instance, value);
                
                if (_editorViewModel != null && !_suppressUndoRecording)
                {
                    _editorViewModel.UndoRedoManager.RecordAction(
                        new ChangePropertyAction(_propertyInfo, _instance, oldValue, value, this));
                }
            }
        }
    }

    // Double value
    private double _doubleValue;
    public double DoubleValue
    {
        get => _doubleValue;
        set
        {
            if (_doubleValue != value)
            {
                object? oldValue = _propertyInfo.GetValue(_instance);
                _doubleValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DoubleValue)));
                _propertyInfo.SetValue(_instance, value);
                
                if (_editorViewModel != null && !_suppressUndoRecording)
                {
                    _editorViewModel.UndoRedoManager.RecordAction(
                        new ChangePropertyAction(_propertyInfo, _instance, oldValue, value, this));
                }
            }
        }
    }

    // Float value
    private float _floatValue;
    public float FloatValue
    {
        get => _floatValue;
        set
        {
            if (_floatValue != value)
            {
                object? oldValue = _propertyInfo.GetValue(_instance);
                _floatValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FloatValue)));
                _propertyInfo.SetValue(_instance, value);
                
                if (_editorViewModel != null && !_suppressUndoRecording)
                {
                    _editorViewModel.UndoRedoManager.RecordAction(
                        new ChangePropertyAction(_propertyInfo, _instance, oldValue, value, this));
                }
            }
        }
    }

    // Enum values
    public List<object>? EnumValues { get; set; }
    public bool IsFlagsEnum { get; set; }
    public ObservableCollection<FlagsEnumItemViewModel>? FlagsEnumItems { get; set; }

    public void UpdateFlagsValue()
    {
        if (_isUpdatingFlags || !IsFlagsEnum || FlagsEnumItems == null)
            return;
            
        _isUpdatingFlags = true;
        
        try
        {
            object? oldValue = _propertyInfo.GetValue(_instance);
            
            int combinedValue = 0;
            foreach (FlagsEnumItemViewModel item in FlagsEnumItems)
            {
                if (item.IsChecked)
                {
                    combinedValue |= Convert.ToInt32(item.EnumValue);
                }
            }
            
            object newValue = Enum.ToObject(PropertyType, combinedValue);
            _propertyInfo.SetValue(_instance, newValue);
            
            if (_editorViewModel != null && !_suppressUndoRecording)
            {
                _editorViewModel.UndoRedoManager.RecordAction(
                    new ChangePropertyAction(_propertyInfo, _instance, oldValue, newValue, this));
            }
        }
        finally
        {
            _isUpdatingFlags = false;
        }
    }

    private object? _selectedEnumValue;
    public object? SelectedEnumValue
    {
        get => _selectedEnumValue;
        set
        {
            if (_selectedEnumValue != value)
            {
                object? oldValue = _propertyInfo.GetValue(_instance);
                _selectedEnumValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedEnumValue)));
                if (value != null)
                {
                    _propertyInfo.SetValue(_instance, value);
                    
                    if (_editorViewModel != null && !_suppressUndoRecording)
                    {
                        _editorViewModel.UndoRedoManager.RecordAction(
                            new ChangePropertyAction(_propertyInfo, _instance, oldValue, value, this));
                    }
                }
            }
        }
    }

    public bool IsString => PropertyType == typeof(string);
    public bool IsBool => PropertyType == typeof(bool);
    public bool IsInt => PropertyType == typeof(int);
    public bool IsDouble => PropertyType == typeof(double);
    public bool IsFloat => PropertyType == typeof(float);
    public bool IsEnum => PropertyType.IsEnum && !IsFlagsEnum;
    public bool IsRegularEnum => PropertyType.IsEnum && !IsFlagsEnum;

    private void UpdatePropertyValue(string newValue)
    {
        try
        {
            object? convertedValue = null;

            if (PropertyType == typeof(string))
            {
                convertedValue = newValue;
            }
            else if (PropertyType == typeof(int))
            {
                convertedValue = int.Parse(newValue);
            }
            else if (PropertyType == typeof(double))
            {
                convertedValue = double.Parse(newValue);
            }
            else if (PropertyType == typeof(float))
            {
                convertedValue = float.Parse(newValue);
            }
            else if (PropertyType == typeof(bool))
            {
                convertedValue = bool.Parse(newValue);
            }
            else
            {
                // Try generic conversion
                convertedValue = Convert.ChangeType(newValue, PropertyType);
            }

            _propertyInfo.SetValue(_instance, convertedValue);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set property {Name}: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class PendingConnectionViewModel : INotifyPropertyChanged
{
    private readonly EditorViewModel _editor;
    private ConnectorViewModel? _source;

    public ConnectorViewModel? Source => _source;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PendingConnectionViewModel(EditorViewModel editor)
    {
        _editor = editor;
        StartCommand = new DelegateCommand<ConnectorViewModel>(source => 
        {
            _source = source;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
        });
        FinishCommand = new DelegateCommand<ConnectorViewModel>(target =>
        {
            bool connected = _editor.Connect(_source, target);

            _source = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
        });
    }

    public ICommand StartCommand { get; }
    public ICommand FinishCommand { get; }

    public void ClearSource()
    {
        _source = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
    }
}

public class DelegateCommand<T> : ICommand
{
    private readonly Action<T> _action;
    private readonly Func<T, bool>? _condition;

    public event EventHandler? CanExecuteChanged;

    public DelegateCommand(Action<T> action, Func<T, bool>? executeCondition = default)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _condition = executeCondition;
    }

    public bool CanExecute(object? parameter)
    {
        if (parameter is T value)
        {
            return _condition?.Invoke(value) ?? true;
        }

        return _condition?.Invoke(default!) ?? true;
    }

    public void Execute(object? parameter)
    {
        if (parameter is T value)
        {
            _action(value);
        }
        else
        {
            _action(default!);
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, new EventArgs());
}

// Clipboard data classes for copy/paste functionality
public class NodeClipboardData
{
    public string TypeName { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = [];
    public Dictionary<string, int> ArraySizes { get; set; } = []; // Property name -> array size
    public List<ConnectionClipboardData>? Connections { get; set; }
    public double RelativeX { get; set; }
    public double RelativeY { get; set; }
}

public class ConnectionClipboardData
{
    public int SourceNodeIndex { get; set; }
    public string SourceConnectorName { get; set; } = "";
    public int TargetNodeIndex { get; set; }
    public string TargetConnectorName { get; set; } = "";
    public int TargetArrayIndex { get; set; } = -1; // -1 means not an array element
}
