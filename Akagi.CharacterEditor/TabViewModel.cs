using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Akagi.CharacterEditor;

public class TabViewModel : INotifyPropertyChanged
{
    private string _displayName = "Untitled";
    private bool _isDirty;
    private string? _filePath;
    private Point _viewportLocation = new(0, 0);
    private double _viewportZoom = 1.0;
    private string _graphId = Guid.NewGuid().ToString();

    public EditorViewModel EditorViewModel { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName != value)
            {
                _displayName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabHeader)));
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabHeader)));
            }
        }
    }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
                UpdateDisplayName();
            }
        }
    }

    public Point ViewportLocation
    {
        get => _viewportLocation;
        set
        {
            if (_viewportLocation != value)
            {
                _viewportLocation = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewportLocation)));
            }
        }
    }

    public double ViewportZoom
    {
        get => _viewportZoom;
        set
        {
            if (_viewportZoom != value)
            {
                _viewportZoom = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewportZoom)));
            }
        }
    }

    public string GraphId
    {
        get => _graphId;
        set
        {
            if (_graphId != value)
            {
                _graphId = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GraphId)));
            }
        }
    }

    public string TabHeader => IsDirty ? $"{DisplayName}*" : DisplayName;

    public ICommand CloseCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TabViewModel(EditorViewModel editorViewModel, ICommand closeCommand)
    {
        EditorViewModel = editorViewModel;
        CloseCommand = closeCommand;

        // Subscribe to changes in the editor to track dirty state
        editorViewModel.Nodes.CollectionChanged += (s, e) => MarkAsDirty();
        editorViewModel.Connections.CollectionChanged += (s, e) => MarkAsDirty();
        
        // Subscribe to undo/redo manager to track dirty state
        editorViewModel.UndoRedoManager.StacksChanged += (s, e) => MarkAsDirty();
    }

    private void MarkAsDirty()
    {
        IsDirty = true;
    }

    public void MarkAsClean()
    {
        IsDirty = false;
    }

    private void UpdateDisplayName()
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(FilePath);
        }
    }
}
