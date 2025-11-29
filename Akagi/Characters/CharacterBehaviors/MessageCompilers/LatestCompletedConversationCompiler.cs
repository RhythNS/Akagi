using Akagi.Receivers;

namespace Akagi.Characters.CharacterBehaviors.MessageCompilers;

internal class LatestCompletedConversationCompiler : MessageCompiler
{
    public override void FilterCompile(Context context, ref List<Conversation> filteredConversations)
    {
        filteredConversations = [.. filteredConversations
            .Where(c => c.IsCompleted)
            .OrderByDescending(c => c.Time)
            .Take(1)];
    }
}
