
using Akagi.LLMs;

namespace Akagi.Communication.Commands.Lists;

internal class ListLLMDefinitions : ListCommand
{
    public override string Name => "/listLLMDefinitions";

    public override string Description => "Lists all available LLM definitions.";

    private readonly ILLMDefinitionDatabase _llmDefinitionDatabase;

    public ListLLMDefinitions(ILLMDefinitionDatabase llmDefinitionDatabase)
    {
        _llmDefinitionDatabase = llmDefinitionDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        List<LLMDefinition> definitions = await _llmDefinitionDatabase.GetDocumentsAsync();

        string[] ids = [.. definitions.Select((def) => def.Id!)];
        string[] names = [.. definitions.Select(def => $"{def.Type}:{def.Model}")];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Available LLM Definitions:\n{choices}");
        return CommandResult.Ok;
    }
}
