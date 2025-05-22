using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Puppeteers;
using Akagi.Puppeteers.SystemProcessors;
using Akagi.Users;

namespace Akagi.Communication;

internal abstract class Communicator : ICommunicator
{
    private readonly IPuppeteer _puppeteer;
    private readonly ISystemProcessorDatabase _systemProcessorDatabase;

    protected Communicator(IPuppeteer puppeteer, ISystemProcessorDatabase systemProcessorDatabase)
    {
        _puppeteer = puppeteer;
        _systemProcessorDatabase = systemProcessorDatabase;
    }

    public abstract Task SendMessage(User user, Character character, string message);
    public abstract Task SendMessage(User user, Character character, Message message);

    protected async Task RecieveMessage(User user, Character character, string message)
    {
        SystemProcessor systemProcessor = await _systemProcessorDatabase.GetSystemProcessor(user, character);

        await _puppeteer.OnMessageRecieved(this, systemProcessor, user, character, message);
    }
}
