using Akagi.LLMs;

namespace Akagi.Communication.Commands.Lists;

internal class DeleteLLMDefinitionCommand : TextCommand
{
    public override string Name => "/deleteLLMDefinition";

    public override string Description => "Deletes an existing LLM definition. Usage: /deleteLLMDefinition <id>";

    public override bool AdminOnly => true;

    private readonly ILLMDefinitionDatabase _lLMDefinitionDatabase;

    public DeleteLLMDefinitionCommand(ILLMDefinitionDatabase lLMDefinitionDatabase)
    {
        _lLMDefinitionDatabase = lLMDefinitionDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length != 1)
        {
            await Communicator.SendMessage(context.User, "Expected an id as argument!");
            return;
        }
        string id = args[0];

        LLMDefinition? definition = await _lLMDefinitionDatabase.GetDocumentByIdAsync(id);
        if (definition == null)
        {
            await Communicator.SendMessage(context.User, $"No LLM definition found with id {id}!");
            return;
        }

        await _lLMDefinitionDatabase.DeleteDocumentByIdAsync(id);

        await Communicator.SendMessage(context.User, $"LLM definition {definition.Type}:{definition.Model} with id {id} deleted successfully!");
    }
}
