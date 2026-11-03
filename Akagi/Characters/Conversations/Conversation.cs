using Akagi.Characters.Conversations;
using Akagi.Data;

namespace Akagi.Characters;

/// <summary>
/// How a conversation was finished, which decides what the character may learn from it.
/// </summary>
internal enum ConversationEndMode
{
    /// <summary>Still running, or ended the usual way.</summary>
    Default,
    /// <summary>All reflectors ran over it.</summary>
    Reflect,
    /// <summary>Kept for display only; nothing is reflected, remembered or fed back to the model.</summary>
    Incognito,
    /// <summary>The user's own summary was stored instead of an automatic reflection.</summary>
    CustomSummary
}

internal class Conversation : DirtyTrackable
{
    private int _id;
    private DateTime _time = DateTime.MinValue;
    private List<Message> _messages = [];
    private bool _isCompleted = false;
    private bool _isSession = false;
    private string _sessionTopic = string.Empty;
    private ConversationEndMode _endMode = ConversationEndMode.Default;
    private DateTime? _endedAt;

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
    /// <summary>
    /// Started explicitly with /session start; only the user can end it.
    /// </summary>
    public virtual bool IsSession
    {
        get => _isSession;
        set => SetProperty(ref _isSession, value);
    }
    public virtual string SessionTopic
    {
        get => _sessionTopic;
        set => SetProperty(ref _sessionTopic, value);
    }
    public virtual ConversationEndMode EndMode
    {
        get => _endMode;
        set => SetProperty(ref _endMode, value);
    }
    public virtual DateTime? EndedAt
    {
        get => _endedAt;
        set => SetProperty(ref _endedAt, value);
    }

    /// <summary>
    /// A running session defers reflection until the user decides how to end it.
    /// </summary>
    public bool IsActiveSession => IsSession && !IsCompleted;

    /// <summary>
    /// Incognito conversations stay visible in the history but never reach reflectors or prompts again.
    /// </summary>
    public bool IsIncognito => IsCompleted && EndMode == ConversationEndMode.Incognito;

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
            switch (message)
            {
                case TextMessage textMessage:
                    bridgeMessages.Add(textMessage.ToBridgeMessage());
                    break;

                case VoiceMessage voiceMessage:
                    bridgeMessages.Add(new Bridge.Chat.Models.TextMessage
                    {
                        Time = voiceMessage.Time,
                        From = Message.ToBridgeType(voiceMessage.From),
                        Text = string.IsNullOrWhiteSpace(voiceMessage.Content) ? "(voice message)" : voiceMessage.Content
                    });
                    break;

                default:
                    // Tool calls (CommandMessage) and their results are the character's own workings, not part of the chat
                    break;
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
            IsCompleted = IsCompleted,
            IsSession = IsSession,
            SessionTopic = SessionTopic,
            EndMode = EndMode,
            EndedAt = EndedAt
        };
        foreach (Message message in Messages)
        {
            copy.AddMessage(message.Copy());
        }
        return copy;
    }
}
