using Akagi.Characters.Conversations;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class DefaultMessageCompiler : MessageCompiler
{
    public override Task<List<Conversation>> FilterCompileAsync(Context context, List<Conversation> filteredConversations)
    {
        return Task.FromResult(filteredConversations);
    }
}
