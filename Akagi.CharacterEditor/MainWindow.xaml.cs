using Nodify;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Akagi.CharacterEditor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private NodeTypeViewModel? _draggedNodeType;
    private readonly MainWindowViewModel? _mainViewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        _mainViewModel = new MainWindowViewModel();
        DataContext = _mainViewModel;

        // Add key down handler for delete key
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // Add closing handler to prompt for unsaved changes
        Closing += MainWindow_Closing;

        // Add keyboard shortcuts
        CommandBindings.Add(new CommandBinding(ApplicationCommands.New, (s, e) => _mainViewModel.NewCommand.Execute(null)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, (s, e) => _mainViewModel.OpenCommand.Execute(null)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (s, e) => _mainViewModel.SaveCommand.Execute(null)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SaveAs, (s, e) => _mainViewModel.SaveAsCommand.Execute(null)));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (s, e) =>
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            viewModel?.CopyNodesCommand.Execute(null);
        }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (s, e) =>
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            viewModel?.PasteNodesCommand.Execute(null);
        }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (s, e) => 
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            viewModel?.UndoCommand.Execute(null);
        }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (s, e) => 
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            viewModel?.RedoCommand.Execute(null);
        }));

        InputBindings.Add(new KeyBinding(ApplicationCommands.New, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Open, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.SaveAs, Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, Key.V, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Undo, Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Y, ModifierKeys.Control));
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_mainViewModel != null)
        {
            bool canClose = _mainViewModel.CloseAllTabs();
            e.Cancel = !canClose;
        }
    }

    private EditorViewModel? GetCurrentEditorViewModel()
    {
        return _mainViewModel?.SelectedTab?.EditorViewModel;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null)
            {
                viewModel.DeleteNodesCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null)
            {
                viewModel.CopyNodesCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null)
            {
                viewModel.PasteNodesCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null)
            {
                viewModel.UndoCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null)
            {
                viewModel.RedoCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void NodeLibraryTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeView treeView && 
            treeView.SelectedItem is NodeTypeViewModel nodeTypeViewModel)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null && !nodeTypeViewModel.IsAbstract)
            {
                // Create node at center of the editor
                viewModel.NodeLibrary.CreateNode(nodeTypeViewModel, new Point(400, 200));
            }
        }
    }

    private void NodeLibraryTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;

        if (sender is TreeView)
        {
            DependencyObject? element = e.OriginalSource as DependencyObject;
            TreeViewItem? treeViewItem = FindParent<TreeViewItem>(element);
            
            if (treeViewItem != null && treeViewItem.DataContext is NodeTypeViewModel nodeType)
            {
                _draggedNodeType = nodeType;
            }
        }
    }

    private void NodeLibraryTreeView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && _draggedNodeType != null)
        {
            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (!_draggedNodeType.IsAbstract && GetCurrentEditorViewModel() != null)
                {
                    _isDragging = true;

                    DataObject dragData = new("NodeTypeViewModel", _draggedNodeType);
                    DragDrop.DoDragDrop((DependencyObject)sender, dragData, DragDropEffects.Copy);

                    _isDragging = false;
                    _draggedNodeType = null;
                }
            }
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent)
            {
                return parent;
            }
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void NodifyEditor_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("NodeTypeViewModel") && sender is NodifyEditor editor)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            if (viewModel != null && e.Data.GetData("NodeTypeViewModel") is NodeTypeViewModel nodeTypeViewModel && !nodeTypeViewModel.IsAbstract)
            {
                // Get the drop position relative to the editor
                Point dropPosition = e.GetPosition(editor);
                
                // Convert to graph coordinates by accounting for viewport location and zoom
                Point graphPosition = new(
                    (dropPosition.X / editor.ViewportZoom) + editor.ViewportLocation.X,
                    (dropPosition.Y / editor.ViewportZoom) + editor.ViewportLocation.Y
                );
                
                viewModel.NodeLibrary.CreateNode(nodeTypeViewModel, graphPosition);
            }
        }
    }

    private void LineConnection_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is LineConnection connection &&
            connection.DataContext is ConnectionViewModel connectionViewModel)
        {
            EditorViewModel? editorViewModel = GetCurrentEditorViewModel();
            if (editorViewModel != null)
            {
                editorViewModel.DeleteConnectionCommand.Execute(connectionViewModel);
                e.Handled = true; // Prevent event from bubbling up
            }
        }
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        Regex regex = GetInteger();
        e.Handled = regex.IsMatch(e.Text);
    }

    private void DecimalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        Regex regex = GetDecimal();
        e.Handled = regex.IsMatch(e.Text);
    }

    private void NodifyEditor_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not NodifyEditor editor)
        {
            return;
        }

        EditorViewModel? viewModel = GetCurrentEditorViewModel();
        if (viewModel == null)
        {
            return;
        }

        // Check if we clicked on empty space or on a pending connection
        DependencyObject? source = e.OriginalSource as DependencyObject;
        
        // Check if we're dragging a connection
        if (viewModel.PendingConnection.Source != null)
        {
            // Create filtered context menu
            Point clickPosition = e.GetPosition(editor);
            ShowFilteredNodeContextMenu(editor, clickPosition, viewModel, viewModel.PendingConnection.Source);
            viewModel.PendingConnection.ClearSource();
            e.Handled = true;
        }
        else if (!IsClickOnNode(source))
        {
            // Create full context menu for empty space
            Point clickPosition = e.GetPosition(editor);
            ShowNodeLibraryContextMenu(editor, clickPosition, viewModel);
            e.Handled = true;
        }
    }

    private void NodifyEditor_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not NodifyEditor editor)
        {
            return;
        }

        EditorViewModel? viewModel = GetCurrentEditorViewModel();
        if (viewModel == null)
        {
            return;
        }

        // Check if we have a pending connection
        if (viewModel.PendingConnection.Source != null)
        {
            DependencyObject? target = e.OriginalSource as DependencyObject;
            
            // Check if we didn't click on a connector
            if (!IsClickOnConnector(target))
            {
                // Create filtered context menu
                Point clickPosition = e.GetPosition(editor);
                ShowFilteredNodeContextMenu(editor, clickPosition, viewModel, viewModel.PendingConnection.Source);
                viewModel.PendingConnection.ClearSource();
                e.Handled = true;
            }
        }
    }

    private static bool IsClickOnConnector(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Nodify.NodeInput || source is Nodify.NodeOutput)
            {
                return true;
            }
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static bool IsClickOnNode(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Nodify.ItemContainer)
            {
                return true;
            }
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void ShowNodeLibraryContextMenu(NodifyEditor editor, Point position, EditorViewModel viewModel)
    {
        // Convert screen position to graph coordinates
        Point graphPosition = new(
            (position.X / editor.ViewportZoom) + editor.ViewportLocation.X,
            (position.Y / editor.ViewportZoom) + editor.ViewportLocation.Y
        );

        ContextMenu contextMenu = new()
        {
            Background = (System.Windows.Media.Brush)FindResource("NodifyEditor.BackgroundBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("NodifyEditor.ForegroundBrush")
        };

        List<NodeTypeViewModel> rootTypes = viewModel.NodeLibrary.GetAllNodeTypesHierarchical();
        
        foreach (NodeTypeViewModel nodeType in rootTypes)
        {
            MenuItem menuItem = CreateMenuItemForNodeType(nodeType, graphPosition, viewModel);
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.PlacementTarget = editor;
        contextMenu.IsOpen = true;
    }

    private void ShowFilteredNodeContextMenu(NodifyEditor editor, Point position, EditorViewModel viewModel, ConnectorViewModel connector)
    {
        // Convert screen position to graph coordinates
        Point graphPosition = new(
            (position.X / editor.ViewportZoom) + editor.ViewportLocation.X,
            (position.Y / editor.ViewportZoom) + editor.ViewportLocation.Y
        );

        ContextMenu contextMenu = new()
        {
            Background = (System.Windows.Media.Brush)FindResource("NodifyEditor.BackgroundBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("NodifyEditor.ForegroundBrush")
        };

        List<NodeTypeViewModel> filteredTypes = viewModel.NodeLibrary.GetFilteredNodeTypesForConnector(connector);
        
        if (filteredTypes.Count == 0)
        {
            MenuItem emptyItem = new()
            {
                Header = "No compatible nodes",
                IsEnabled = false,
                Style = (Style)FindResource("ThemedMenuItem")
            };
            contextMenu.Items.Add(emptyItem);
        }
        else
        {
            // Group by base type
            Dictionary<string, List<NodeTypeViewModel>> groupedTypes = [];
            
            foreach (NodeTypeViewModel nodeType in filteredTypes)
            {
                if (!groupedTypes.TryGetValue(nodeType.BaseTypeName, out List<NodeTypeViewModel>? value))
                {
                    value = [];
                    groupedTypes[nodeType.BaseTypeName] = value;
                }

                value.Add(nodeType);
            }

            foreach (KeyValuePair<string, List<NodeTypeViewModel>> group in groupedTypes)
            {
                if (group.Value.Count == 1)
                {
                    // Single item, add directly
                    MenuItem menuItem = CreateLeafMenuItem(group.Value[0], graphPosition, viewModel, connector);
                    contextMenu.Items.Add(menuItem);
                }
                else
                {
                    // Multiple items, create submenu
                    MenuItem groupMenuItem = new()
                    {
                        Header = group.Key,
                        Style = (Style)FindResource("ThemedMenuItem")
                    };

                    foreach (NodeTypeViewModel nodeType in group.Value)
                    {
                        MenuItem subItem = CreateLeafMenuItem(nodeType, graphPosition, viewModel, connector);
                        groupMenuItem.Items.Add(subItem);
                    }

                    contextMenu.Items.Add(groupMenuItem);
                }
            }
        }

        contextMenu.PlacementTarget = editor;
        contextMenu.IsOpen = true;
    }

    private MenuItem CreateMenuItemForNodeType(NodeTypeViewModel nodeType, Point position, EditorViewModel viewModel)
    {
        MenuItem menuItem = new()
        {
            Header = nodeType.DisplayName,
            Style = (Style)FindResource("ThemedMenuItem")
        };

        if (nodeType.IsAbstract)
        {
            menuItem.IsEnabled = false;
            menuItem.FontStyle = FontStyles.Italic;
            menuItem.Foreground = System.Windows.Media.Brushes.Gray;
        }
        else
        {
            menuItem.Click += (s, e) =>
            {
                viewModel.NodeLibrary.CreateNode(nodeType, position);
            };
        }

        // Add children as submenu items
        if (nodeType.Children.Count > 0)
        {
            foreach (NodeTypeViewModel child in nodeType.Children)
            {
                MenuItem childItem = CreateMenuItemForNodeType(child, position, viewModel);
                menuItem.Items.Add(childItem);
            }
        }

        return menuItem;
    }

    private MenuItem CreateLeafMenuItem(NodeTypeViewModel nodeType, Point position, EditorViewModel viewModel, ConnectorViewModel? connector)
    {
        MenuItem menuItem = new()
        {
            Header = nodeType.DisplayName,
            Style = (Style)FindResource("ThemedMenuItem")
        };

        menuItem.Click += (s, e) =>
        {
            NodeViewModel newNode = NodeFactory.CreateNodeFromType(nodeType.NodeType, position.X, position.Y);
            viewModel.UndoRedoManager.RecordAction(new UndoRedo.AddNodeAction(viewModel.Nodes, newNode));
            viewModel.Nodes.Add(newNode);

            // Try to connect automatically if we have a connector
            if (connector != null)
            {
                // Find a compatible connector on the new node
                ConnectorViewModel? targetConnector = null;

                // If source is an output, look for inputs on the new node
                if (connector.ParentNode != null && connector.ParentNode.Output.Contains(connector))
                {
                    targetConnector = newNode.Input.FirstOrDefault(c => 
                        c.AllowedType != null && 
                        connector.AllowedType != null &&
                        c.AllowedType.IsAssignableFrom(connector.AllowedType));
                }
                // If source is an input, look for outputs on the new node
                else
                {
                    targetConnector = newNode.Output.FirstOrDefault(c =>
                        c.AllowedType != null &&
                        connector.AllowedType != null &&
                        connector.AllowedType.IsAssignableFrom(c.AllowedType));
                }

                if (targetConnector != null)
                {
                    viewModel.Connect(connector, targetConnector);
                }
            }
        };

        return menuItem;
    }

    private void ItemContainer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Nodify.ItemContainer container && container.DataContext is NodeViewModel nodeViewModel)
        {
            nodeViewModel.BeginMove();
        }
    }

    private void ItemContainer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Nodify.ItemContainer container && container.DataContext is NodeViewModel nodeViewModel)
        {
            EditorViewModel? viewModel = GetCurrentEditorViewModel();
            // Delay the EndMove call to allow Nodify to finish updating the location
            container.Dispatcher.BeginInvoke(new Action(() =>
            {
                nodeViewModel.EndMove(viewModel);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    [GeneratedRegex("[^0-9-]+")]
    private static partial Regex GetInteger();
    [GeneratedRegex("[^0-9.-]+")]
    private static partial Regex GetDecimal();
}

public class CollectionCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CollectionHasItemsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }
}

public class CollectionCountToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 1 ? new GridLength(300) : new GridLength(0);
        }
        return new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class SnapToGridConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool snapToGrid && snapToGrid)
        {
            return 20.0;
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TypeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string typeName && !string.IsNullOrEmpty(typeName))
        {
            return GenerateColorFromString(typeName);
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static SolidColorBrush GenerateColorFromString(string input)
    {
        // Use MD5 hash to generate consistent color from string
        byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(input));

        // Use first 3 bytes for RGB, adjust to ensure readable colors
        byte r = (byte)(hashBytes[0] * 0.7 + 76); // Range: 76-255
        byte g = (byte)(hashBytes[1] * 0.7 + 76); // Range: 76-255
        byte b = (byte)(hashBytes[2] * 0.7 + 76); // Range: 76-255

        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null && !string.IsNullOrWhiteSpace(value.ToString())
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
