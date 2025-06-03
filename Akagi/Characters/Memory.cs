using Akagi.Data;

namespace Akagi.Characters;

internal class Memory : DirtyTrackable
{
    private List<Thought> _thoughts = [];

    public IReadOnlyList<Thought> Thoughts
    {
        get => _thoughts;
        set => SetProperty(ref _thoughts, [.. value]);
    }

    public void AddThought(Thought thought)
    {
        Dirty = true;
        _thoughts.Add(thought);
    }

    public Thought AddThought(string text)
    {
        Thought thought = new()
        {
            Content = text,
            Timestamp = DateTime.Now
        };
        Dirty = true;
        _thoughts.Add(thought);
        return thought;
    }

    public void ClearThoughts()
    {
        Dirty = true;
        _thoughts.Clear();
    }

    public void RemoveThought(Thought thought)
    {
        if (_thoughts.Remove(thought))
        {
            Dirty = true;
        }
    }

    public void RemoveThoughtAt(int index)
    {
        if (index >= 0 && index < _thoughts.Count)
        {
            Dirty = true;
            _thoughts.RemoveAt(index);
        }
    }
}
