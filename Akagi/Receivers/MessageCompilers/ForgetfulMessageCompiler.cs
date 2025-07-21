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

    public override Message[] Compile(User user, Character character)
    {
        List<Message> messages = [];

        IEnumerable<Conversation> conversations = character.Conversations.OrderByDescending(x => x.Time);
        foreach (Conversation conversation in conversations)
        {
            if (messages.Count >= MaxMessages)
                break;
            foreach (Message message in conversation.Messages
                                                    .Where(x => (x.VisibleTo & ReadableMessages) != 0)
                                                    .OrderByDescending(x => x.Time))
            {
                if (messages.Count >= MaxMessages)
                    break;
                if (message.VisibleTo.HasFlag(Message.Type.Character) || message.VisibleTo.HasFlag(Message.Type.User))
                {
                    messages.Add(message);
                }
            }
        }

        messages.Reverse();

        return [.. messages];
    }
}
