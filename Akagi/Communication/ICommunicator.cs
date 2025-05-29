using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication.Commands;
using Akagi.Users;

namespace Akagi.Communication;

internal interface ICommunicator
{
    public string Name { get; }
    public Command[] AvailableCommands { get; }

    public Task SendMessage(User user, Character character, string message);
    public Task SendMessage(User user, Character character, Message message);
    public Task SendMessage(User user, string message);
    public Task SendMessage(User user, Message message);
}
