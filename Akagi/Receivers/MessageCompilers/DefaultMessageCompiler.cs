using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;

namespace Akagi.Receivers.MessageCompilers;

internal class DefaultMessageCompiler : MessageCompiler
{
    public override Message[] Compile(User user, Character character)
    {
        List<Message> messages = [];

        IEnumerable<Conversation> conversations = character.Conversations.OrderBy(x => x.Time);
        foreach (Conversation conversation in conversations)
        {
            foreach (Message message in conversation.Messages
                                                    .Where(x => (x.VisibleTo & ReadableMessages) != 0)
                                                    .OrderBy(x => x.Time))
            {
                messages.Add(message);
            }
        }
        return [.. messages];
    }
}
