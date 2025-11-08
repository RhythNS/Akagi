using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Users;

namespace Akagi.Receivers.MessageCompilers;

internal class ForgetfulMessageCompiler : MessageCompiler
{
    private int _maxMessages;

    public int MaxMessages
    {
        get => _maxMessages;
        set => SetProperty(ref _maxMessages, value);
    }

    public override void FilterCompile(User user, Character character, ref List<Conversation> filteredConversations)
    {
        List<Message> messages = [];
        IEnumerable<Conversation> conversations = filteredConversations.OrderByDescending(x => x.Time);
        foreach (Conversation conversation in conversations)
        {
            if (messages.Count >= MaxMessages)
            {
                break;
            }

            foreach (Message message in conversation.Messages
                .Where(x => (x.VisibleTo & ReadableMessages) != 0)
                .OrderByDescending(x => x.Time))
            {
                if (messages.Count >= MaxMessages)
                {
                    break;
                }

                messages.Add(message);
            }
        }
        messages.Reverse();

        filteredConversations.Clear();
        Conversation newConversation = new()
        {
            Id = 0,
            Time = DateTime.UtcNow,
            Messages = [.. messages]
        };
        filteredConversations.Add(newConversation);
    }
}
