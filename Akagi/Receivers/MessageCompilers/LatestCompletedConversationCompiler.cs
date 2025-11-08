using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Receivers.MessageCompilers;

internal class LatestCompletedConversationCompiler : MessageCompiler
{
    public override void FilterCompile(User user, Character character, ref List<Conversation> filteredConversations)
    {
        filteredConversations = [.. filteredConversations
            .Where(c => c.IsCompleted)
            .OrderByDescending(c => c.Time)
            .Take(1)];
    }
}
