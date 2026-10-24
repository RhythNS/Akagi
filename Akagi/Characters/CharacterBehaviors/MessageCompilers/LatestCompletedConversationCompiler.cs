using Akagi.Characters.Conversations;
using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class LatestCompletedConversationCompiler : MessageCompiler
{
    public override Task<List<Conversation>> FilterCompileAsync(Context context, List<Conversation> filteredConversations)
    {
        List<Conversation> result = [.. filteredConversations
            .Where(c => c.IsCompleted)
            .OrderByDescending(c => c.Time)
            .Take(1)];

        return Task.FromResult(result);
    }
}
