namespace Akagi.CharacterEditor.UndoRedo;

public class UndoRedoManager
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private bool _isUndoRedoInProgress;

    public event EventHandler? StacksChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void RecordAction(IUndoableAction action)
    {
        if (_isUndoRedoInProgress)
        {
            return;
        }

        _undoStack.Push(action);
        _redoStack.Clear();
        OnStacksChanged();
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _isUndoRedoInProgress = true;
        try
        {
            IUndoableAction action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);
            OnStacksChanged();
        }
        finally
        {
            _isUndoRedoInProgress = false;
        }
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _isUndoRedoInProgress = true;
        try
        {
            IUndoableAction action = _redoStack.Pop();
            action.Redo();
            _undoStack.Push(action);
            OnStacksChanged();
        }
        finally
        {
            _isUndoRedoInProgress = false;
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnStacksChanged();
    }

    private void OnStacksChanged()
    {
        StacksChanged?.Invoke(this, EventArgs.Empty);
    }
}
