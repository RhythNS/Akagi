using Akagi.Characters.Conversations;
using Akagi.Data;

namespace Akagi.Characters;

internal class Conversation : DirtyTrackable
{
    private int _id;
    private DateTime _time = DateTime.MinValue;
    private List<Message> _messages = [];
    private bool _isCompleted = false;

    public virtual int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }
    public virtual DateTime Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }
    public virtual IReadOnlyList<Message> Messages
    {
        get => _messages;
        set => SetProperty(ref _messages, [.. value]);
    }
    public virtual bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    public virtual void AddMessage(Message message)
    {
        Dirty = true;
        _messages.Add(message);
    }

    public virtual void InsertMessage(int index, Message message)
    {
        Dirty = true;
        _messages.Insert(index, message);
    }

    public virtual IReadOnlyList<Message> GetLastMessages(int count)
    {
        return [.. Messages.Skip(Math.Max(0, Messages.Count - count))];
    }

    public virtual void ClearMessages()
    {
        Dirty = true;
        _messages.Clear();
    }

    public virtual bool RemoveMessage(Message message)
    {
        if (!_messages.Remove(message))
        {
            return false;
        }

        Dirty = true;
        return true;
    }

    public virtual bool RemoveMessageAt(int index)
    {
        if (index < 0 || index >= _messages.Count)
        {
            return false;
        }

        Dirty = true;
        _messages.RemoveAt(index);
        return true;
    }

    public virtual bool EditMessage(int index, string newText)
    {
        if (index < 0 || index >= _messages.Count || _messages[index] is not TextMessage textMessage)
        {
            return false;
        }

        Dirty = true;
        textMessage.Text = newText;
        return true;
    }

    public virtual int CountWords(Message.Type filter)
    {
        return _messages.Where(x => x.From.HasFlag(filter))
            .Sum(x => x.CountWords());
    }

    public virtual Bridge.Chat.Models.Conversation ToBridgeModel()
    {
        List<Bridge.Chat.Models.TextMessage> bridgeMessages = [];
        foreach (Message message in _messages)
        {
            if (message is TextMessage textMessage)
            {
                bridgeMessages.Add(textMessage.ToBridgeMessage());
            }
            else
            {
                throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}");
            }
        }

        return new Bridge.Chat.Models.Conversation
        {
            Id = Id,
            Time = Time,
            Messages = bridgeMessages,
            IsCompleted = IsCompleted
        };
    }

    public virtual Conversation Copy()
    {
        Conversation copy = new()
        {
            Id = Id,
            Time = Time,
            IsCompleted = IsCompleted
        };
        foreach (Message message in Messages)
        {
            copy.AddMessage(message.Copy());
        }
        return copy;
    }
}
