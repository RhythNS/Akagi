using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Receivers.SystemProcessors;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Receivers.Puppeteers;

internal class LinePuppeteer : Puppeteer
{
    public class Definition
    {
        [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
        public required string SystemProcessorId { get; set; }
        [BsonIgnore]
        public SystemProcessor? SystemProcessor { get; set; } = null;
    }

    private Definition[] _definitions = [];
    public Definition[] Definitions
    {
        get => _definitions;
        set => SetProperty(ref _definitions, value);
    }

    protected override async Task InnerInit()
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            Definitions[i].SystemProcessor = await GetSingle(Definitions[i].SystemProcessorId)
                ?? throw new InvalidOperationException($"System processor with ID {Definitions[i].SystemProcessorId} not found.");
        }
    }

    public override async Task ProcessAsync()
    {
        foreach (Definition definition in Definitions)
        {
            bool shouldContinue = true;
            do
            {
                Command[] commands = await LLM.GetNextSteps(definition!.SystemProcessor!, Character, User);
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
}
