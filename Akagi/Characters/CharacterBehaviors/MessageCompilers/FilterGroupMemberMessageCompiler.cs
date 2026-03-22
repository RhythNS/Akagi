using Akagi.Bridge.Attributes;
using Akagi.Characters.Cards;
using Akagi.Characters.Conversations;
using Akagi.Receivers;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

[GraphNode]
internal class FilterGroupMemberMessageCompiler : MessageCompiler
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

    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        List<Conversation> result = [];

        foreach (Conversation conversation in filteredConversations)
        {
            conversation.Messages = [.. conversation.Messages.Where(IsAllowed)];
            if (conversation.Messages.Count > 0)
            {
                result.Add(conversation);
            }
        }

        filteredConversations = result;
    }

    private bool IsAllowed(Message message)
    {
        if (message.CharacterId == null)
        {
            return AllowUserMessages;
        }

        return AllowedCharacterIds.Length == 0 || AllowedCharacterIds.Contains(message.CharacterId);
    }
}
