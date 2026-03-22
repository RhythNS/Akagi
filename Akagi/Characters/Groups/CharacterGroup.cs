using Akagi.Bridge.Attributes;
using Akagi.Characters.CharacterBehaviors.Interceptors;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups.Strategies;
using Akagi.Characters.Memories;
using Akagi.Data;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Groups;

[GraphNode]
internal class CharacterGroup : Savable
{
    private string _name = string.Empty;
    private string _userId = string.Empty;
    private string[] _memberCharacterIds = [];
    private string _strategyId = string.Empty;
    private ConversationContainer _conversations = new();
    private Memory _sharedMemory = new();
    private bool _allowAutomaticProcessing = true;
    private GroupVisibility _userVisibility = GroupVisibility.FinalOnly | GroupVisibility.CompletionNotice;
    private GroupErrorBehavior _errorBehavior = GroupErrorBehavior.Abort;
    private string _errorMessage = "An error occurred while processing: {{error}}";
    private string[] _interceptorIds = [];
    private string[] _reflectorIds = [];

    public override bool Dirty
    {
        get => base.Dirty || _sharedMemory.Dirty || _conversations.Dirty;
        set
        {
            base.Dirty = value;
            if (value == false)
            {
                _sharedMemory.Dirty = false;
                _conversations.Dirty = false;
            }
        }
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    [NodeReference(typeof(User))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    [UserId]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    [NodeReference(typeof(Character))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] MemberCharacterIds
    {
        get => _memberCharacterIds;
        set => SetProperty(ref _memberCharacterIds, value);
    }
    [NodeReference(typeof(GroupStrategy))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string StrategyId
    {
        get => _strategyId;
        set => SetProperty(ref _strategyId, value);
    }
    public ConversationContainer Conversations
    {
        get => _conversations;
        set => SetProperty(ref _conversations, value);
    }
    public Memory SharedMemory
    {
        get => _sharedMemory;
        set => SetProperty(ref _sharedMemory, value);
    }
    public bool AllowAutomaticProcessing
    {
        get => _allowAutomaticProcessing;
        set => SetProperty(ref _allowAutomaticProcessing, value);
    }
    public GroupVisibility UserVisibility
    {
        get => _userVisibility;
        set => SetProperty(ref _userVisibility, value);
    }
    public GroupErrorBehavior ErrorBehavior
    {
        get => _errorBehavior;
        set => SetProperty(ref _errorBehavior, value);
    }
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }
    [NodeReference(typeof(Interceptor))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] InterceptorIds
    {
        get => _interceptorIds;
        set => SetProperty(ref _interceptorIds, value);
    }
    [NodeReference(typeof(Reflector))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] ReflectorIds
    {
        get => _reflectorIds;
        set => SetProperty(ref _reflectorIds, value);
    }
}
