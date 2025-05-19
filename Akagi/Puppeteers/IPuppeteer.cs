using Akagi.Characters;
using Akagi.Communication;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;

namespace Akagi.Puppeteers;

internal interface IPuppeteer
{
    public Task OnMessageRecieved(ICommunicator from, SystemProcessor processor, User user, Character character, string message);
    public Task OnMessageIgnored(SystemProcessor processor, Character character, User user);
    public Task Reflect(SystemProcessor processor, Character character, User user);
}
