using Akagi.Data;

namespace Akagi.Characters.Memories;

internal class Memory : DirtyTrackable
{
    private ThoughtCollection<SingleFactThought> _goals = new();
    private ThoughtCollection<SingleFactThought> _shortTerm = new();
    private ThoughtCollection<SingleFactThought> _longTerm = new();
    private ThoughtCollection<ConversationThought> _conversations = new();

    public ThoughtCollection<SingleFactThought> Goals
    {
        get => _goals;
        set => SetProperty(ref _goals, value);
    }
    public ThoughtCollection<SingleFactThought> ShortTerm
    {
        get => _shortTerm;
        set => SetProperty(ref _shortTerm, value);
    }

    public ThoughtCollection<SingleFactThought> LongTerm
    {
        get => _longTerm;
        set => SetProperty(ref _longTerm, value);
    }

    public ThoughtCollection<ConversationThought> Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }

    public override bool Dirty
    {
        get => base.Dirty || _goals.Dirty || _shortTerm.Dirty || _longTerm.Dirty || _conversations.Dirty;
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _goals.Dirty = false;
                _shortTerm.Dirty = false;
                _longTerm.Dirty = false;
                _conversations.Dirty = false;
            }
        }
    }
}
