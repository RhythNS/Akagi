using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;

namespace Akagi.Characters;

internal class Conversation
{
    public DateTime Time { get; set; }
    public List<Message> Messages { get; set; } = [];
    public bool IsCompleted { get; set; } = false;

    public void AddMessage(Message message)
    {
        Messages.Add(message);
    }

    public Message AddMessage(string text, DateTime time, Message.Type from)
    {
        TextMessage message = new()
        {
            Text = text,
            From = from,
            Time = time
        };

        Messages.Add(message);

        return message;
    }

#pragma warning disable IDE0060 // Remove unused parameter TODO: fix this
    public Message AddCommand(Command command, DateTime time, Message.Type from)
#pragma warning restore IDE0060 // Remove unused parameter
    {
        if (command is MessageCommand messageCommand)
        {
            Message message = messageCommand.GetMessage();
            Messages.Add(message);
            return message;
        }
     
        throw new NotImplementedException();
    }

    public IReadOnlyList<Message> GetLastMessages(int count)
    {
        return Messages.Skip(Math.Max(0, Messages.Count - count)).ToList();
    }
}
