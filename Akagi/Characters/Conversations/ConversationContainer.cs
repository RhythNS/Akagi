using Akagi.Data;

namespace Akagi.Characters.Conversations;

internal class ConversationContainer : DirtyTrackable
{
    private List<Conversation> _list = [];
    public IReadOnlyList<Conversation> List
    {
        get => _list;
        set => SetProperty(ref _list, [.. value]);
    }

    public override bool Dirty
    {
        get => base.Dirty || _list.Any(c => c.Dirty);
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _list.ForEach(c => c.Dirty = false);
            }
        }
    }

    public Conversation GetCurrentConversation()
    {
        if (List.Count != 0)
        {
            Conversation? conv = List.MaxBy(c => c.Time);
            if (conv != null && conv.IsCompleted == false)
            {
                return conv;
            }
        }

        Dirty = true;
        _list.Add(new Conversation
        {
            Time = DateTime.UtcNow,
            Messages = []
        });
        return List[^1];
    }

    public Conversation StartNewConversation()
    {
        Conversation? conv = List.MaxBy(c => c.Time);
        if (conv != null)
        {
            conv.IsCompleted = true;
        }

        Conversation newConversation = new()
        {
            Time = DateTime.UtcNow,
            Messages = [],
            Id = List.Count > 0 ? List.Max(c => c.Id) + 1 : 1,
        };

        Dirty = true;
        _list.Add(newConversation);
        return newConversation;
    }

    public void CompleteCurrentConversationAtIndex(int messageIndex)
    {
        Conversation? conv = List.MaxBy(c => c.Time);
        if (conv == null)
        {
            return;
        }
        if (messageIndex < 0 || messageIndex >= conv.Messages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(messageIndex), "Message index is out of range.");
        }
        List<Message> completedMessages = [.. conv.Messages.Take(messageIndex + 1)];
        Conversation completedConversation = new()
        {
            Id = conv.Id,
            Time = completedMessages.Max(x => x.Time),
            Messages = completedMessages,
            IsCompleted = true
        };

        List<Message> remainingMessages = [.. conv.Messages.Skip(messageIndex + 1)];
        Conversation remainingConversation = new()
        {
            Id = List.Count > 0 ? List.Max(c => c.Id) + 1 : 1,
            Time = remainingMessages.Count > 0 ? remainingMessages.Max(x => x.Time) : DateTime.UtcNow,
            Messages = remainingMessages,
            IsCompleted = false
        };

        Dirty = true;
        _list.Remove(conv);
        _list.Add(completedConversation);
        _list.Add(remainingConversation);
    }

    public void ClearCurrentConversation()
    {
        Conversation? conv = List.MaxBy(c => c.Time);
        if (conv == null)
        {
            return;
        }

        Dirty = true;
        _list.Remove(conv);
    }

    public ConversationContainer Copy()
    {
        return new ConversationContainer
        {
            List = List.Select(c => c.Copy()).ToList()
        };
    }
}
