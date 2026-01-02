namespace Akagi.CharacterEditor.UndoRedo;

public interface IUndoableAction
{
    void Undo();
    void Redo();
    string Description { get; }
}
