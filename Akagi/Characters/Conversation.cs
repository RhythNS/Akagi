using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.Commands.Messages;

namespace Akagi.Characters;

internal class Conversation
{
    public DateTime Time { get; set; }
    public List<Message> Messages { get; set; } = [];

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

    public Message AddCommand(Command command, DateTime time, Message.Type from)
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
