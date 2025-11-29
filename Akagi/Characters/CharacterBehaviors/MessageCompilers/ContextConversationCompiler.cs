using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class ContextConversationCompiler : MessageCompiler
{
    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        filteredConversations.Clear();
        filteredConversations.Add(context.Conversation);
    }
}
