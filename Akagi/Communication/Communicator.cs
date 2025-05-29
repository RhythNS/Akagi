using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication.Commands;
using Akagi.Receivers;
using Akagi.Users;

namespace Akagi.Communication;

internal abstract class Communicator : ICommunicator
{
    public abstract string Name { get; }
    public abstract Command[] AvailableCommands { get; }

    private readonly IReceiver _receiver;

    protected Communicator(IReceiver receiver)
    {
        _receiver = receiver;
    }

    public abstract Task SendMessage(User user, Character character, string message);
    public abstract Task SendMessage(User user, Character character, Message message);
    public abstract Task SendMessage(User user, string message);
    public abstract Task SendMessage(User user, Message message);

    protected Task RecieveMessage(User user, Character character, string message)
    {
        TextMessage textMessage = new()
        {
            From = Message.Type.User,
            Text = message,
            Time = DateTime.UtcNow,
        };
        return RecieveMessage(user, character, textMessage);
    }

    protected async Task RecieveMessage(User user, Character character, Message message)
    {
        await _receiver.OnMessageRecieved(this, user, character, message);
    }
}
