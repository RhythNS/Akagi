using Akagi.Characters.Conversations;

namespace Akagi.Characters;

internal class AggregateConversation : Conversation
{
    private readonly Conversation _master;
    private readonly IReadOnlyList<Conversation> _targets;

    public AggregateConversation(Conversation master, IEnumerable<Conversation> targets)
    {
        _master = master;
        _targets = [.. targets];
    }

    public override int Id
    {
        get => _master.Id;
        set => _master.Id = value;
    }

    public override DateTime Time
    {
        get => _master.Time;
        set => _master.Time = value;
    }

    public override IReadOnlyList<Message> Messages
    {
        get => _master.Messages;
        set => _master.Messages = value;
    }

    public override bool IsCompleted
    {
        get => _master.IsCompleted;
        set => _master.IsCompleted = value;
    }

    public override bool Dirty
    {
        get => _master.Dirty || _targets.Any(t => t.Dirty);
        set
        {
            _master.Dirty = value;
            foreach (Conversation target in _targets)
            {
                target.Dirty = value;
            }
        }
    }

    public override void AddMessage(Message message)
    {
        _master.AddMessage(message);
        foreach (Conversation target in _targets)
        {
            target.AddMessage(message.Copy());
        }
    }

    public override void InsertMessage(int index, Message message)
    {
        _master.InsertMessage(index, message);
        foreach (Conversation target in _targets)
        {
            target.InsertMessage(index, message.Copy());
        }
    }

    public override IReadOnlyList<Message> GetLastMessages(int count)
    {
        return _master.GetLastMessages(count);
    }

    public override void ClearMessages()
    {
        _master.ClearMessages();
        foreach (Conversation target in _targets)
        {
            target.ClearMessages();
        }
    }

    public override bool RemoveMessage(Message message)
    {
        bool removed = false;
        removed |= _master.RemoveMessage(message);
        foreach (Conversation target in _targets)
        {
            removed |= target.RemoveMessage(message);
        }

        return removed;
    }

    public override bool RemoveMessageAt(int index)
    {
        bool removed = false;
        removed |= _master.RemoveMessageAt(index);
        foreach (Conversation target in _targets)
        {
            removed |= target.RemoveMessageAt(index);
        }

        return removed;
    }

    public override bool EditMessage(int index, string newText)
    {
        bool edited = false;
        edited |= _master.EditMessage(index, newText);
        foreach (Conversation target in _targets)
        {
            edited |= target.EditMessage(index, newText);
        }

        return edited;
    }

    public override int CountWords(Message.Type filter)
    {
        return _master.CountWords(filter);
    }

    public override Bridge.Chat.Models.Conversation ToBridgeModel()
    {
        return _master.ToBridgeModel();
    }
}
