using Akagi.Characters.Cards;
using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters;

internal class Character : Savable
{
    private List<Conversation> _conversations = [];
    private Memory _memory = new();
    private string _cardId = string.Empty;
    private Card? _card;
    private string _puppeteerId = string.Empty;
    private string[] _reflectorIds = [];
    private string _userId = string.Empty;
    private string[] _triggerPointIds = [];
    private bool _allowAutomaticProcessing = true;
    private string _name = string.Empty;

    public override bool Dirty
    {
        get => base.Dirty || _memory.Dirty || _conversations.Any(c => c.Dirty);
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _memory.Dirty = false;
                _conversations.ForEach(c => c.Dirty = false);
            }
        }
    }

    public IReadOnlyList<Conversation> Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, [.. value]);
    }
    public Memory Memory
    {
        get => _memory;
        set => SetProperty(ref _memory, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string CardId
    {
        get => _cardId;
        set => SetProperty(ref _cardId, value);
    }
    public Card Card => _card ?? throw new InvalidOperationException("Card is not set. Ensure to set the Card property after retrieving the Character from the database.");
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string PuppeteerId
    {
        get => _puppeteerId;
        set => SetProperty(ref _puppeteerId, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] ReflectorIds
    {
        get => _reflectorIds;
        set => SetProperty(ref _reflectorIds, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] TriggerPointIds
    {
        get => _triggerPointIds;
        set => SetProperty(ref _triggerPointIds, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public bool AllowAutomaticProcessing
    {
        get => _allowAutomaticProcessing;
        set => SetProperty(ref _allowAutomaticProcessing, value);
    }
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public void Init(Card card)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card), "Card cannot be null.");
    }

    public Conversation GetCurrentConversation()
    {
        if (Conversations.Count != 0)
        {
            Conversation? conv = Conversations.MaxBy(c => c.Time);
            if (conv != null && conv.IsCompleted == false)
            {
                return conv;
            }
        }

        Dirty = true;
        _conversations.Add(new Conversation
        {
            Time = DateTime.UtcNow,
            Messages = []
        });
        return Conversations[0];
    }

    public Conversation StartNewConversation()
    {
        Conversation? conv = Conversations.MaxBy(c => c.Time);
        if (conv != null)
        {
            conv.IsCompleted = true;
        }

        Conversation newConversation = new()
        {
            Time = DateTime.UtcNow,
            Messages = [],
            Id = Conversations.Count > 0 ? Conversations.Max(c => c.Id) + 1 : 1,
        };

        Dirty = true;
        _conversations.Add(newConversation);
        return newConversation;
    }

    public void CompleteCurrentConversationAtIndex(int messageIndex)
    {
        Conversation? conv = Conversations.MaxBy(c => c.Time);
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
            Id = Conversations.Count > 0 ? Conversations.Max(c => c.Id) + 1 : 1,
            Time = remainingMessages.Count > 0 ? remainingMessages.Max(x => x.Time) : DateTime.UtcNow,
            Messages = remainingMessages,
            IsCompleted = false
        };

        Dirty = true;
        _conversations.Remove(conv);
        _conversations.Add(completedConversation);
        _conversations.Add(remainingConversation);
    }

    public void ClearCurrentConversation()
    {
        Conversation? conv = Conversations.MaxBy(c => c.Time);
        if (conv == null)
        {
            return;
        }

        Dirty = true;
        _conversations.Remove(conv);
    }
}
