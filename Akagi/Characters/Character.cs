using Akagi.Bridge.Attributes;
using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.Interceptors;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Characters.TriggerPoints;
using Akagi.Data;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters;

[GraphNode]
internal class Character : Savable
{
    private ConversationContainer _conversations = new();
    private Memory _memory = new();
    private string _cardId = string.Empty;
    private Card? _card;
    private string _puppeteerId = string.Empty;
    private string[] _reflectorIds = [];
    private string[] _interceptorIds = [];
    private string _userId = string.Empty;
    private string[] _triggerPointIds = [];
    private bool _allowAutomaticProcessing = true;
    private bool _allowVoice = false;
    private string _voiceId = string.Empty;
    private string _voiceModelId = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;

    public override bool Dirty
    {
        get => base.Dirty || _memory.Dirty || _conversations.Dirty;
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _memory.Dirty = false;
                _conversations.Dirty = false;
            }
        }
    }

    public ConversationContainer Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }
    public Memory Memory
    {
        get => _memory;
        set => SetProperty(ref _memory, value);
    }
    [NodeReference(typeof(Card))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string CardId
    {
        get => _cardId;
        set => SetProperty(ref _cardId, value);
    }
    public Card Card => _card ?? throw new InvalidOperationException("Card is not set. Ensure to set the Card property after retrieving the Character from the database.");
    [NodeReference(typeof(Puppeteer))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string PuppeteerId
    {
        get => _puppeteerId;
        set => SetProperty(ref _puppeteerId, value);
    }
    [NodeReference(typeof(Reflector))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] ReflectorIds
    {
        get => _reflectorIds;
        set => SetProperty(ref _reflectorIds, value);
    }
    [NodeReference(typeof(TriggerPoint))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] TriggerPointIds
    {
        get => _triggerPointIds;
        set => SetProperty(ref _triggerPointIds, value);
    }
    [NodeReference(typeof(Interceptor))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] InterceptorIds
    {
        get => _interceptorIds;
        set => SetProperty(ref _interceptorIds, value);
    }
    [NodeReference(typeof(User))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    [UserId]
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
    public bool AllowVoice
    {
        get => _allowVoice;
        set => SetProperty(ref _allowVoice, value);
    }
    public string VoiceId
    {
        get => _voiceId;
        set => SetProperty(ref _voiceId, value);
    }
    public string VoiceModelId
    {
        get => _voiceModelId;
        set => SetProperty(ref _voiceModelId, value);
    }
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public void Init(Card card)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card), "Card cannot be null.");
    }
}
