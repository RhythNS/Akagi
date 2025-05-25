using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Users;

namespace Akagi.Receivers;

internal interface IReceiver
{
    public Task OnMessageRecieved(ICommunicator from, User user, Character character, Message message);
    public Task OnMessageIgnored(Character character, User user);
    public Task Reflect(Character character, User user);
}
