using Akagi.Data;

namespace Akagi.Characters.Memories;

internal class ThoughtCollection<T> : DirtyTrackable where T : Thought
{
    private List<T> _thoughts = [];

    public IReadOnlyList<T> Thoughts
    {
        get => _thoughts;
        set => SetProperty(ref _thoughts, [.. value]);
    }

    public void AddThought(T thought)
    {
        Dirty = true;
        _thoughts.Add(thought);
    }

    public void ClearThoughts()
    {
        Dirty = true;
        _thoughts.Clear();
    }

    public void RemoveThought(T thought)
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

    public override bool Dirty
    {
        get
        {
            if (base.Dirty)
            {
                return true;
            }
            foreach (T thought in _thoughts)
            {
                if (thought.Dirty)
                {
                    return true;
                }
            }
            return false;
        }
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                foreach (T thought in _thoughts)
                {
                    thought.Dirty = false;
                }
            }
        }
    }
}
