using Akagi.LLMs;
using System.Collections.ObjectModel;

namespace Akagi.Communication.Commands.Users;

internal class ChangeLLMCommand : TextCommand
{
    public override string Name => "/changeLLM";

    public override string Description => "Changes your preferred LLM. Usage: /changeLLM <Preference index> <LLM id>";

    private readonly ILLMDefinitionDatabase _llmDefinitions;

    public ChangeLLMCommand(ILLMDefinitionDatabase llmDefinitions)
    {
        _llmDefinitions = llmDefinitions;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(context.User, "Please provide the preference index and the LLM index to switch to.");
            return;
        }
        if (!int.TryParse(args[0], out int preferenceIndex))
        {
            await Communicator.SendMessage(context.User, "Invalid preference index. Please provide a valid number.");
            return;
        }
        ILLM.LLMUsage[] usages = Enum.GetValues<ILLM.LLMUsage>();
        if (preferenceIndex < 0 || preferenceIndex >= usages.Length)
        {
            string valuesList = string.Join(", ", usages.Select((u, i) => $"{i}: {u}"));
            await Communicator.SendMessage(context.User, $"Invalid preference index. Please provide a valid number. Valid values are: {valuesList}");
            return;
        }
        string llmId = args[1];
        LLMDefinition? llm = await _llmDefinitions.GetDocumentByIdAsync(llmId);
        if (llm == null)
        {
            await Communicator.SendMessage(context.User, "Could not find llm with that id!");
            return;
        }

        Dictionary<string, string> dict = context.User.LLMPreferences.ToDictionary();
        dict[usages[preferenceIndex].ToString()] = llm.Id!;
        context.User.LLMPreferences = new ReadOnlyDictionary<string, string>(dict);

        await Communicator.SendMessage(context.User, $"Your preferred LLM has been changed to {llm.Type}:{llm.Model}.");
    }
}
