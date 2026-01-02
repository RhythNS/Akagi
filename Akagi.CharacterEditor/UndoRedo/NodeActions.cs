using System.Collections.ObjectModel;
using System.Windows;

namespace Akagi.CharacterEditor.UndoRedo;

public class AddNodeAction : IUndoableAction
{
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly NodeViewModel _node;

    public string Description => $"Add {_node.Title}";

    public AddNodeAction(ObservableCollection<NodeViewModel> nodes, NodeViewModel node)
    {
        _nodes = nodes;
        _node = node;
    }

    public void Undo()
    {
        _nodes.Remove(_node);
    }

    public void Redo()
    {
        _nodes.Add(_node);
    }
}

public class RemoveNodeAction : IUndoableAction
{
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly NodeViewModel _node;
    private readonly int _index;

    public string Description => $"Remove {_node.Title}";

    public RemoveNodeAction(ObservableCollection<NodeViewModel> nodes, NodeViewModel node)
    {
        _nodes = nodes;
        _node = node;
        _index = nodes.IndexOf(node);
    }

    public void Undo()
    {
        if (_index >= 0 && _index <= _nodes.Count)
        {
            _nodes.Insert(_index, _node);
        }
        else
        {
            _nodes.Add(_node);
        }
    }

    public void Redo()
    {
        _nodes.Remove(_node);
    }
}

public class MoveNodeAction : IUndoableAction
{
    private readonly NodeViewModel _node;
    private readonly Point _oldLocation;
    private readonly Point _newLocation;

    public string Description => $"Move {_node.Title}";

    public MoveNodeAction(NodeViewModel node, Point oldLocation, Point newLocation)
    {
        _node = node;
        _oldLocation = oldLocation;
        _newLocation = newLocation;
    }

    public void Undo()
    {
        _node.Location = _oldLocation;
    }

    public void Redo()
    {
        _node.Location = _newLocation;
    }
}

public class ChangeArraySizeAction : IUndoableAction
{
    private readonly ArrayConnectorGroup _arrayGroup;
    private readonly int _oldSize;
    private readonly int _newSize;
    private readonly EditorViewModel _editorViewModel;
    private readonly List<ConnectionViewModel> _removedConnections = [];

    public string Description => $"Change {_arrayGroup.Title} size from {_oldSize} to {_newSize}";

    public ChangeArraySizeAction(ArrayConnectorGroup arrayGroup, int oldSize, int newSize, EditorViewModel editorViewModel)
    {
        _arrayGroup = arrayGroup;
        _oldSize = oldSize;
        _newSize = newSize;
        _editorViewModel = editorViewModel;
    }

    public void Redo()
    {
        Execute();
    }

    public void Execute()
    {
        // When reducing size, store removed connections
        if (_newSize < _oldSize)
        {
            _removedConnections.Clear();
            for (int i = _newSize; i < _arrayGroup.Elements.Count; i++)
            {
                ConnectorViewModel element = _arrayGroup.Elements[i];
                List<ConnectionViewModel> connections = [.. _editorViewModel.Connections.Where(c => c.Target == element)];
                _removedConnections.AddRange(connections);
            }
        }

        _arrayGroup.SetArraySize(_newSize, _editorViewModel);
    }

    public void Undo()
    {
        _arrayGroup.SetArraySize(_oldSize);
        
        // Restore removed connections
        foreach (ConnectionViewModel connection in _removedConnections)
        {
            _editorViewModel.Connections.Add(connection);
            connection.Source.IsConnected = true;
            connection.Target.IsConnected = true;
        }
    }
}
