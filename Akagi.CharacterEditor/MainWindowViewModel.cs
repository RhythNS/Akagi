using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Akagi.CharacterEditor;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private TabViewModel? _selectedTab;
    private int _untitledCounter = 1;
    private bool _snapToGrid;

    public UserConfig UserConfig { get; private set; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab != value)
            {
                _selectedTab = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTab)));
            }
        }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set
        {
            if (_snapToGrid != value)
            {
                _snapToGrid = value;
                UserConfig.SnapToGrid = value;
                UserConfig.Save();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SnapToGrid)));
            }
        }
    }

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand OpenRecentFileCommand { get; }

    public ObservableCollection<string> RecentFiles { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly JsonSerializerOptions CachedJsonSerializerOptions = new() { WriteIndented = true };

    public MainWindowViewModel()
    {
        // Load user config
        UserConfig = UserConfig.Load();
        _snapToGrid = UserConfig.SnapToGrid;

        NewCommand = new DelegateCommand<object>(_ => CreateNewTab());
        OpenCommand = new DelegateCommand<object>(_ => OpenFile());
        SaveCommand = new DelegateCommand<object>(_ => SaveFile());
        SaveAsCommand = new DelegateCommand<object>(_ => SaveFileAs());
        CloseTabCommand = new DelegateCommand<TabViewModel>(tab => CloseTab(tab));
        OpenRecentFileCommand = new DelegateCommand<string>(filePath => OpenRecentFile(filePath));

        // Load recent files
        RefreshRecentFiles();

        // Create initial tab
        CreateNewTab();
    }

    private void CreateNewTab()
    {
        EditorViewModel editorViewModel = new();
        editorViewModel.UndoRedoManager.Clear();
        TabViewModel tab = new(editorViewModel, CloseTabCommand)
        {
            DisplayName = $"Untitled {_untitledCounter++}"
        };

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void OpenFile()
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Node Graph Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Open Node Graph"
        };

        if (dialog.ShowDialog() == true)
        {
            OpenFileByPath(dialog.FileName);
        }
    }

    private void OpenRecentFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return;

        if (!File.Exists(filePath))
        {
            MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            UserConfig.RemoveRecentFile(filePath);
            RefreshRecentFiles();
            return;
        }

        OpenFileByPath(filePath);
    }

    private void OpenFileByPath(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            (EditorViewModel editorViewModel, Point viewportLocation, double viewportZoom, string graphId) = DeserializeGraph(json);

            editorViewModel.UndoRedoManager.Clear();

            TabViewModel tab = new(editorViewModel, CloseTabCommand)
            {
                FilePath = filePath,
                ViewportLocation = viewportLocation,
                ViewportZoom = viewportZoom,
                GraphId = graphId
            };
            tab.MarkAsClean();

            Tabs.Add(tab);
            SelectedTab = tab;

            // Add to recent files
            UserConfig.AddRecentFile(filePath);
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveFile()
    {
        if (SelectedTab == null)
            return;

        if (string.IsNullOrEmpty(SelectedTab.FilePath))
        {
            SaveFileAs();
        }
        else
        {
            SaveToFile(SelectedTab.FilePath);
        }
    }

    private void SaveFileAs()
    {
        if (SelectedTab == null)
            return;

        SaveFileDialog dialog = new()
        {
            Filter = "Node Graph Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Save Node Graph",
            FileName = SelectedTab.DisplayName
        };

        if (dialog.ShowDialog() == true)
        {
            SaveToFile(dialog.FileName);
            SelectedTab.FilePath = dialog.FileName;

            // Add to recent files
            AddToRecentFiles(dialog.FileName);
        }
    }

    private void SaveToFile(string filePath)
    {
        if (SelectedTab == null)
            return;

        try
        {
            string json = SerializeGraph(SelectedTab.EditorViewModel, SelectedTab.ViewportLocation, SelectedTab.ViewportZoom, SelectedTab.GraphId);
            File.WriteAllText(filePath, json, Encoding.UTF8);
            SelectedTab.MarkAsClean();

            // Add to recent files
            UserConfig.AddRecentFile(filePath);
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public bool CloseTab(TabViewModel? tab)
    {
        if (tab == null)
            return true;

        if (tab.IsDirty)
        {
            MessageBoxResult result = MessageBox.Show(
                $"Do you want to save changes to '{tab.DisplayName}'?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Select the tab before saving
                SelectedTab = tab;
                SaveFile();
                
                // Check if still dirty (user may have cancelled save dialog)
                if (tab.IsDirty)
                    return false;
            }
            else if (result == MessageBoxResult.Cancel)
            {
                return false;
            }
        }

        Tabs.Remove(tab);

        // Select another tab if available
        if (Tabs.Count > 0 && SelectedTab == null)
        {
            SelectedTab = Tabs[0];
        }

        return true;
    }

    public bool CloseAllTabs()
    {
        while (Tabs.Count > 0)
        {
            if (!CloseTab(Tabs[0]))
                return false;
        }
        return true;
    }

    private void AddToRecentFiles(string filePath)
    {
        // Remove from recent files if already exists
        RecentFiles.Remove(filePath);

        // Add to the top
        RecentFiles.Insert(0, filePath);

        // Keep only 10 recent files
        if (RecentFiles.Count > 10)
        {
            RecentFiles.RemoveAt(10);
        }

        // Update user config
        UserConfig.RecentFiles = [.. RecentFiles];
        UserConfig.Save();
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        foreach (string file in UserConfig.GetValidRecentFiles())
        {
            RecentFiles.Add(file);
        }
    }

    private static string SerializeGraph(EditorViewModel viewModel, Point viewportLocation, double viewportZoom, string graphId)
    {
        GraphData graphData = new()
        {
            GraphId = graphId,
            Nodes = [],
            Connections = [],
            ViewportLocation = new PointData { X = viewportLocation.X, Y = viewportLocation.Y },
            ViewportZoom = viewportZoom
        };

        // Serialize nodes
        Dictionary<NodeViewModel, int> nodeIndices = [];
        int index = 0;
        foreach (NodeViewModel node in viewModel.Nodes)
        {
            nodeIndices[node] = index++;

            if (node.NodeWrapper?.WrappedInstance == null)
                continue;

            NodeData nodeData = new()
            {
                TypeName = node.NodeWrapper.WrappedInstance.GetType().AssemblyQualifiedName ?? "",
                Location = new() { X = node.Location.X, Y = node.Location.Y },
                Properties = [],
                ArraySizes = []
            };

            // Serialize array sizes
            foreach (ArrayConnectorGroup arrayGroup in node.ArrayGroups)
            {
                nodeData.ArraySizes[arrayGroup.PropertyInfo.Name] = arrayGroup.ArraySize;
            }

            // Serialize all properties except NodeReference types
            System.Reflection.PropertyInfo[] properties = node.NodeWrapper.WrappedInstance.GetType()
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (System.Reflection.PropertyInfo property in properties)
            {
                if (property.GetCustomAttribute<Akagi.Bridge.Attributes.NodeReferenceAttribute>() != null)
                    continue;

                if (!property.CanRead || !property.CanWrite)
                    continue;

                try
                {
                    object? value = property.GetValue(node.NodeWrapper.WrappedInstance);
                    if (value != null)
                    {
                        nodeData.Properties[property.Name] = value;
                    }
                }
                catch { }
            }

            graphData.Nodes.Add(nodeData);
        }

        // Serialize connections
        foreach (ConnectionViewModel connection in viewModel.Connections)
        {
            if (connection.Source.ParentNode == null || connection.Target.ParentNode == null)
                continue;

            if (!nodeIndices.TryGetValue(connection.Source.ParentNode, out int sourceNodeIndex) ||
                !nodeIndices.TryGetValue(connection.Target.ParentNode, out int targetNodeIndex))
                continue;

            ConnectionData connectionData = new()
            {
                SourceNodeIndex = sourceNodeIndex,
                SourceConnectorName = connection.Source.PropertyInfo?.Name ?? "",
                TargetNodeIndex = targetNodeIndex,
                TargetConnectorName = connection.Target.PropertyInfo?.Name ?? "",
                TargetArrayIndex = connection.Target.IsArrayElement ? connection.Target.ArrayIndex : -1
            };

            graphData.Connections.Add(connectionData);
        }

        return JsonSerializer.Serialize(graphData, CachedJsonSerializerOptions);
    }

    private static (EditorViewModel, Point, double, string) DeserializeGraph(string json)
    {
        GraphData? graphData = JsonSerializer.Deserialize<GraphData>(json);
        if (graphData == null)
            throw new InvalidDataException("Failed to deserialize graph data");

        EditorViewModel viewModel = new();
        List<NodeViewModel> nodes = [];

        // Deserialize nodes
        foreach (NodeData nodeData in graphData.Nodes)
        {
            Type? nodeType = Type.GetType(nodeData.TypeName);
            if (nodeType == null)
                continue;

            NodeViewModel node = NodeFactory.CreateNodeFromType(nodeType, nodeData.Location.X, nodeData.Location.Y);

            // Set EditorViewModel for all array groups
            foreach (ArrayConnectorGroup arrayGroup in node.ArrayGroups)
            {
                arrayGroup.EditorViewModel = viewModel;
            }

            // Restore array sizes
            foreach (KeyValuePair<string, int> kvp in nodeData.ArraySizes)
            {
                ArrayConnectorGroup? arrayGroup = node.ArrayGroups.FirstOrDefault(ag => ag.PropertyInfo.Name == kvp.Key);
                arrayGroup?.SetArraySize(kvp.Value, viewModel);
            }

            // Restore properties
            if (node.NodeWrapper?.WrappedInstance != null)
            {
                foreach (KeyValuePair<string, object?> kvp in nodeData.Properties)
                {
                    System.Reflection.PropertyInfo? property = nodeType.GetProperty(kvp.Key);
                    if (property != null && property.CanWrite)
                    {
                        try
                        {
                            object? value = kvp.Value;
                            
                            // Handle JsonElement conversion
                            if (value is JsonElement jsonElement)
                            {
                                value = JsonSerializer.Deserialize(jsonElement.GetRawText(), property.PropertyType);
                            }
                            
                            property.SetValue(node.NodeWrapper.WrappedInstance, value);

                            // Update the view model if it's an inline property
                            NodePropertyViewModel? inlineProp = node.InlineProperties.FirstOrDefault(p => p.Name == kvp.Key);
                            if (inlineProp != null)
                            {
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
                                else if (inlineProp.IsBasicCollection && inlineProp.CollectionItems != null && value != null)
                                {
                                    // Update basic collection items
                                    inlineProp.CollectionItems.Clear();
                                    if (value is System.Collections.IEnumerable enumerable)
                                    {
                                        int index = 0;
                                        foreach (object? item in enumerable)
                                        {
                                            inlineProp.CollectionItems.Add(new CollectionItemViewModel(
                                                index++,
                                                item?.ToString() ?? string.Empty,
                                                inlineProp));
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            nodes.Add(node);
            viewModel.Nodes.Add(node);
        }

        // Deserialize connections
        foreach (ConnectionData connectionData in graphData.Connections)
        {
            if (connectionData.SourceNodeIndex >= nodes.Count || connectionData.TargetNodeIndex >= nodes.Count)
                continue;

            NodeViewModel sourceNode = nodes[connectionData.SourceNodeIndex];
            NodeViewModel targetNode = nodes[connectionData.TargetNodeIndex];

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
                viewModel.Connect(sourceConnector, targetConnector);
            }
        }

                // Restore viewport position and zoom
                Point viewportLocation = new(
                    graphData.ViewportLocation?.X ?? 0,
                    graphData.ViewportLocation?.Y ?? 0
                );
                double viewportZoom = graphData.ViewportZoom.HasValue ? graphData.ViewportZoom.Value : 1.0;
                string graphId = string.IsNullOrEmpty(graphData.GraphId) ? Guid.NewGuid().ToString() : graphData.GraphId;

                return (viewModel, viewportLocation, viewportZoom, graphId);
            }
        }

// Data structures for serialization
public class GraphData
{
    public string GraphId { get; set; } = Guid.NewGuid().ToString();
    public List<NodeData> Nodes { get; set; } = [];
    public List<ConnectionData> Connections { get; set; } = [];
    public PointData? ViewportLocation { get; set; }
    public double? ViewportZoom { get; set; }
}

public class NodeData
{
    public string TypeName { get; set; } = "";
    public PointData Location { get; set; } = new();
    public Dictionary<string, object?> Properties { get; set; } = [];
    public Dictionary<string, int> ArraySizes { get; set; } = []; // Property name -> array size
}

public class PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class ConnectionData
{
    public int SourceNodeIndex { get; set; }
    public string SourceConnectorName { get; set; } = "";
    public int TargetNodeIndex { get; set; }
    public string TargetConnectorName { get; set; } = "";
    public int TargetArrayIndex { get; set; } = -1; // -1 means not an array element
}
