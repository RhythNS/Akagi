using System.Collections.ObjectModel;

namespace Akagi.CharacterEditor.UndoRedo;

public class AddConnectionAction : IUndoableAction
{
    private readonly ObservableCollection<ConnectionViewModel> _connections;
    private readonly ConnectionViewModel _connection;

    public string Description => $"Add Connection";

    public AddConnectionAction(ObservableCollection<ConnectionViewModel> connections, ConnectionViewModel connection)
    {
        _connections = connections;
        _connection = connection;
    }

    public void Undo()
    {
        _connections.Remove(_connection);
        _connection.Source.IsConnected = _connections.Any(c => c.Source == _connection.Source || c.Target == _connection.Source);
        _connection.Target.IsConnected = _connections.Any(c => c.Source == _connection.Target || c.Target == _connection.Target);
    }

    public void Redo()
    {
        _connections.Add(_connection);
        _connection.Source.IsConnected = true;
        _connection.Target.IsConnected = true;
    }
}

public class RemoveConnectionAction : IUndoableAction
{
    private readonly ObservableCollection<ConnectionViewModel> _connections;
    private readonly ConnectionViewModel _connection;
    private readonly int _index;

    public string Description => $"Remove Connection";

    public RemoveConnectionAction(ObservableCollection<ConnectionViewModel> connections, ConnectionViewModel connection)
    {
        _connections = connections;
        _connection = connection;
        _index = connections.IndexOf(connection);
    }

    public void Undo()
    {
        if (_index >= 0 && _index <= _connections.Count)
        {
            _connections.Insert(_index, _connection);
        }
        else
        {
            _connections.Add(_connection);
        }
        _connection.Source.IsConnected = true;
        _connection.Target.IsConnected = true;
    }

    public void Redo()
    {
        _connections.Remove(_connection);
        _connection.Source.IsConnected = _connections.Any(c => c.Source == _connection.Source || c.Target == _connection.Source);
        _connection.Target.IsConnected = _connections.Any(c => c.Source == _connection.Target || c.Target == _connection.Target);
    }
}
