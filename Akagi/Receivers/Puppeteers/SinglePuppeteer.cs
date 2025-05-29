using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Receivers.SystemProcessors;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Receivers.Puppeteers;

internal class SinglePuppeteer : Puppeteer
{
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string SystemProcessorId { get; set; } = string.Empty;

    private SystemProcessor? _systemProcessor;

    protected override async Task InnerInit()
    {
        _systemProcessor = await GetSingle(SystemProcessorId);
    }

    public override async Task ProcessAsync()
    {
        if (_systemProcessor == null)
        {
            throw new InvalidOperationException($"System processor with ID {SystemProcessorId} not found.");
        }

        bool shouldContinue = true;
        do
        {
            Command[] commands = await LLM.GetNextSteps(_systemProcessor, Character, User);
            foreach (Command command in commands)
            {
                await command.Execute(Context);

                if (command is MessageCommand response)
                {
                    await Communicator.SendMessage(User, Character, response.GetMessage());
                }

                shouldContinue &= command.ContinueAfterExecution;
            }
        } while (shouldContinue);
    }
}
