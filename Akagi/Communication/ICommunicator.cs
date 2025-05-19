using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Communication;

internal interface ICommunicator
{
    public Task SendMessage(User user, Character character, string message);
    public Task SendMessage(User user, Character character, Message message);
}
