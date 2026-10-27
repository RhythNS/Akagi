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

        Conversation newConv = new()
        {
            Id = List.Count > 0 ? List.Max(c => c.Id) + 1 : 1,
            Time = DateTime.UtcNow,
            Messages = []
        };

        Dirty = true;
        _list.Add(newConv);
        return newConv;
    }

    /// <summary>
    /// The running conversation, without creating one.
    /// </summary>
    public Conversation? Current
    {
        get
        {
            Conversation? conv = List.MaxBy(c => c.Time);
            return conv != null && !conv.IsCompleted ? conv : null;
        }
    }

    public Conversation? ActiveSession => Current is { IsSession: true } session ? session : null;

    /// <summary>
    /// Finishes the running conversation with the given mode and opens a fresh one.
    /// Returns the finished conversation, or null when nothing was running.
    /// </summary>
    public Conversation? EndCurrent(ConversationEndMode mode, DateTime nowUtc)
    {
        Conversation? current = Current;
        if (current == null)
        {
            return null;
        }

        current.EndMode = mode;
        current.EndedAt = nowUtc;
        StartNewConversation();
        return current;
    }

    /// <summary>
    /// Ends the running conversation the usual way and starts a session that stays open until the user ends it.
    /// </summary>
    public Conversation StartSession(string topic, DateTime nowUtc)
    {
        Conversation? current = Current;
        if (current != null)
        {
            current.EndedAt = nowUtc;
        }

        Conversation session = StartNewConversation();
        session.IsSession = true;
        session.SessionTopic = topic;
        return session;
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

    public Conversation CompleteCurrentConversationAtIndex(int messageIndex)
    {
        Conversation? conv = List.MaxBy(c => c.Time);
        if (conv == null)
        {
            return GetCurrentConversation();
        }
        if (messageIndex < 0 || messageIndex >= conv.Messages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(messageIndex), "Message index is out of range.");
        }
        List<Message> completedMessages = [.. conv.Messages.Take(messageIndex + 1)];
        Conversation completedConversation = new()
        {
            Id = conv.Id,
            Time = completedMessages.Count > 0 ? completedMessages.Max(x => x.Time) : conv.Time,
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
        return remainingConversation;
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
        ConversationContainer copy = new()
        {
            List = [.. _list.Select(c => c.Copy())]
        };
        return copy;
    }
}
