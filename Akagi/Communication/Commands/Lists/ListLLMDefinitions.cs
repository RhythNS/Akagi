
using Akagi.LLMs;
using Microsoft.Extensions.Options;

namespace Akagi.Communication.Commands.Lists;

internal class ListLLMDefinitions : ListCommand
{
    public override string Name => "/listLLMDefinitions";

    public override string Description => "Lists all available LLM definitions.";

    private readonly LLMDefinitions _llmDefinitions;

    public ListLLMDefinitions(IOptions<LLMDefinitions> llmDefinitions)
    {
        _llmDefinitions = llmDefinitions.Value;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        LLMDefinition[] definitions = _llmDefinitions.Definitions;

        string[] ids = [.. definitions.Select((_, index) => index.ToString())];
        string[] names = [.. definitions.Select(def => $"{def.Type}:{def.Model}")];
        string choices = GetIdList(ids, names);
        return Communicator.SendMessage(context.User, $"Available LLM Definitions:\n{choices}");
    }
}
