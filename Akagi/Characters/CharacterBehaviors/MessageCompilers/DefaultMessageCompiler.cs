using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class DefaultMessageCompiler : MessageCompiler
{
    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        // No additional processing needed for the default compiler
    }
}
