using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Receivers.MessageCompilers;

internal class DefaultMessageCompiler : MessageCompiler
{
    public override void FilterCompile(User user, Character character, ref List<Conversation> filteredConversations)
    {
        // No additional processing needed for the default compiler
    }
}
