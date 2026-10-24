using Akagi.Bridge.Attributes;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.Conversations;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.Filters;

[GraphNode]
internal abstract class FilterMessageCompiler : MessageCompiler
{
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

    protected abstract bool IsAllowed(Message message);
}
