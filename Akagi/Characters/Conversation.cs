using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;

namespace Akagi.Characters;

internal class Conversation : DirtyTrackable
{
    private int _id;
    private DateTime _time = DateTime.MinValue;
    private List<Message> _messages = [];
    private bool _isCompleted = false;

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }
    public DateTime Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }
    public IReadOnlyList<Message> Messages
    {
        get => _messages;
        set => SetProperty(ref _messages, [.. value]);
    }
    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    public void AddMessage(Message message)
    {
        Dirty = true;
        _messages.Add(message);
    }

    public Message AddMessage(string text, DateTime time, Message.Type from, Message.Type visibleTo)
    {
        TextMessage message = new()
        {
            Text = text,
            From = from,
            Time = time,
            VisibleTo = visibleTo
        };

        Dirty = true;
        _messages.Add(message);

        return message;
    }

#pragma warning disable IDE0060 // Remove unused parameter TODO: fix this
    public Message AddCommand(Command command, DateTime time, Message.Type from)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        if (command is MessageCommand messageCommand)
        {
            Message message = messageCommand.GetMessage();
            Dirty = true;
            _messages.Add(message);
            return message;
        }

        throw new NotImplementedException();
    }

    public IReadOnlyList<Message> GetLastMessages(int count)
    {
        return [.. Messages.Skip(Math.Max(0, Messages.Count - count))];
    }

    public void ClearMessages()
    {
        Dirty = true;
        _messages.Clear();
    }

    public bool RemoveMessage(Message message)
    {
        if (!_messages.Remove(message))
        {
            return false;
        }

        Dirty = true;
        return true;
    }

    public bool RemoveMessageAt(int index)
    {
        if (index < 0 || index >= _messages.Count)
        {
            return false;
        }

        Dirty = true;
        _messages.RemoveAt(index);
        return true;
    }

    public bool EditMessage(int index, string newText)
    {
        if (index < 0 || index >= _messages.Count || _messages[index] is not TextMessage textMessage)
        {
            return false;
        }

        Dirty = true;
        textMessage.Text = newText;
        return true;
    }

    public Bridge.Chat.Models.Conversation ToBridgeModel()
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

    public Conversation Copy()
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
