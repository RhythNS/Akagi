using Akagi.Bridge.Attributes;
using Akagi.Characters.Cards;
using Akagi.Characters.Conversations;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Filters;

[GraphNode]
internal class GroupMemberFilterCompiler : FilterMessageCompiler
{
    private string[] _allowedCharacterIds = [];
    private bool _allowUserMessages = true;

    [NodeReference(typeof(Card))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string[] AllowedCharacterIds
    {
        get => _allowedCharacterIds;
        set => SetProperty(ref _allowedCharacterIds, value);
    }
    public bool AllowUserMessages
    {
        get => _allowUserMessages;
        set => SetProperty(ref _allowUserMessages, value);
    }

    protected override bool IsAllowed(Message message)
    {
        if (message.CharacterId == null)
        {
            return AllowUserMessages;
        }

        return AllowedCharacterIds.Length == 0 || AllowedCharacterIds.Contains(message.CharacterId);
    }
}
