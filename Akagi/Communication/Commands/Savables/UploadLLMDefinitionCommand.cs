using Akagi.LLMs;
using MongoDB.Driver;
using static Akagi.LLMs.ILLM;

namespace Akagi.Communication.Commands.Savables;

internal class UploadLLMDefinitionCommand : TextCommand
{
    public override string Name => "/uploadLLMDefinition";

    public override string Description => "Creates a new LLM definition. Usage /uploadLLMDefinition <LLMProvider> <Model>";

    public override bool AdminOnly => true;

    private readonly ILLMDefinitionDatabase _lLMDefinitionDatabase;

    public UploadLLMDefinitionCommand(ILLMDefinitionDatabase lLMDefinitionDatabase)
    {
        _lLMDefinitionDatabase = lLMDefinitionDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length != 2)
        {
            await Communicator.SendMessage(context.User, "Expected LLMProvider and Model as arguments!");
            return CommandResult.Fail("Invalid arguments.");
        }
        if (Enum.TryParse(args[0], out LLMType result) == false)
        {
            await Communicator.SendMessage(context.User, $"Unknown LLMProvider. Valid options are {string.Join(",", Enum.GetValues<LLMType>())}");
            return CommandResult.Fail("Unknown LLM provider.");
        }
        string model = args[1];

        FilterDefinition<LLMDefinition> filter =
            Builders<LLMDefinition>.Filter.And(
                Builders<LLMDefinition>.Filter.Eq(x => x.Type, result),
                Builders<LLMDefinition>.Filter.Eq(x => x.Model, model)
            );
        List<LLMDefinition> existingDefinitions = await _lLMDefinitionDatabase.GetDocumentsByPredicateAsync(filter);
        if (existingDefinitions.Count != 0)
        {
            await Communicator.SendMessage(context.User, $"This LLM already exists with id {existingDefinitions[0].Id}!");
            return CommandResult.Fail("LLM definition already exists.");
        }

        LLMDefinition definition = new()
        {
            Model = model,
            Type = result
        };
        await _lLMDefinitionDatabase.SaveDocumentAsync(definition);

        await Communicator.SendMessage(context.User, $"LLMDefinition created with id {definition.Id!}");
        return CommandResult.Ok;
    }
}
