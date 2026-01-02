using System.Collections.ObjectModel;

namespace Akagi.CharacterEditor.UndoRedo;

public class CompositeAction : IUndoableAction
{
    private readonly List<IUndoableAction> _actions = [];
    private readonly string _description;

    public string Description => _description;

    public CompositeAction(string description)
    {
        _description = description;
    }

    public void AddAction(IUndoableAction action)
    {
        _actions.Add(action);
    }

    public void Undo()
    {
        // Undo in reverse order
        for (int i = _actions.Count - 1; i >= 0; i--)
        {
            _actions[i].Undo();
        }
    }

    public void Redo()
    {
        // Redo in forward order
        foreach (IUndoableAction action in _actions)
        {
            action.Redo();
        }
    }
}

public class DeleteNodesAction : IUndoableAction
{
    private readonly ObservableCollection<NodeViewModel> _nodes;
    private readonly ObservableCollection<ConnectionViewModel> _connections;
    private readonly List<NodeViewModel> _deletedNodes = [];
    private readonly List<(ConnectionViewModel connection, int index)> _deletedConnections = [];

    public string Description => $"Delete {_deletedNodes.Count} node(s)";

    public DeleteNodesAction(ObservableCollection<NodeViewModel> nodes, ObservableCollection<ConnectionViewModel> connections, IEnumerable<NodeViewModel> nodesToDelete)
    {
        _nodes = nodes;
        _connections = connections;
        _deletedNodes.AddRange(nodesToDelete);

        // Capture connections that will be deleted
        foreach (NodeViewModel node in _deletedNodes)
        {
            List<ConnectionViewModel> nodeConnections = [.. connections.Where(c => c.Source.ParentNode == node || c.Target.ParentNode == node)];

            foreach (ConnectionViewModel connection in nodeConnections)
            {
                int index = connections.IndexOf(connection);
                _deletedConnections.Add((connection, index));
            }
        }
    }

    public void Undo()
    {
        // Restore nodes
        foreach (NodeViewModel node in _deletedNodes)
        {
            _nodes.Add(node);
        }

        // Restore connections in their original order
        foreach ((ConnectionViewModel connection, int index) in _deletedConnections.OrderBy(x => x.index))
        {
            if (index >= 0 && index <= _connections.Count)
            {
                _connections.Insert(index, connection);
            }
            else
            {
                _connections.Add(connection);
            }
            connection.Source.IsConnected = true;
            connection.Target.IsConnected = true;
        }
    }

    public void Redo()
    {
        // Remove connections
        foreach ((ConnectionViewModel connection, int _) in _deletedConnections)
        {
            _connections.Remove(connection);
        }

        // Update IsConnected status for remaining connections
        foreach ((ConnectionViewModel connection, int _) in _deletedConnections)
        {
            connection.Source.IsConnected = _connections.Any(c => c.Source == connection.Source || c.Target == connection.Source);
            connection.Target.IsConnected = _connections.Any(c => c.Source == connection.Target || c.Target == connection.Target);
        }

        // Remove nodes
        foreach (NodeViewModel node in _deletedNodes)
        {
            _nodes.Remove(node);
        }
    }
}
