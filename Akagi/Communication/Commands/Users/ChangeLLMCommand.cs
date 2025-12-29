using Akagi.LLMs;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;

namespace Akagi.Communication.Commands.Users;

internal class ChangeLLMCommand : TextCommand
{
    public override string Name => "/changeLLM";

    public override string Description => "Changes your preferred LLM. Usage: /changeLLM <Preference index> <LLM index>";

    private readonly LLMDefinitions _llmDefinitions;

    public ChangeLLMCommand(IOptions<LLMDefinitions> llmDefinitions)
    {
        _llmDefinitions = llmDefinitions.Value;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            return Communicator.SendMessage(context.User, "Please provide the preference index and the LLM index to switch to.");
        }
        if (!int.TryParse(args[0], out int preferenceIndex))
        {
            return Communicator.SendMessage(context.User, "Invalid preference index. Please provide a valid number.");
        }
        ILLM.LLMUsage[] usages = Enum.GetValues<ILLM.LLMUsage>();
        if (preferenceIndex < 0 || preferenceIndex >= usages.Length)
        {
            string valuesList = string.Join(", ", usages.Select((u, i) => $"{i}: {u}"));
            return Communicator.SendMessage(context.User, $"Invalid preference index. Please provide a valid number. Valid values are: {valuesList}");
        }
        if (!int.TryParse(args[1], out int llmIndex))
        {
            return Communicator.SendMessage(context.User, "Invalid LLM index. Please provide a valid number.");
        }
        if (llmIndex < 0 || llmIndex >= _llmDefinitions.Definitions.Length)
        {
            return Communicator.SendMessage(context.User, $"LLM index out of range. Please provide a number between 0 and {_llmDefinitions.Definitions.Length - 1}.");
        }

        LLMDefinition llmDefinition = _llmDefinitions.Definitions[llmIndex];
        Dictionary<string, LLMDefinition> dict = context.User.LLMPreferences.ToDictionary();
        dict[usages[preferenceIndex].ToString()] = llmDefinition;
        context.User.LLMPreferences = new ReadOnlyDictionary<string, LLMDefinition>(dict);

        return Communicator.SendMessage(context.User, $"Your preferred LLM has been changed to {llmDefinition.Type}:{llmDefinition.Model}.");
    }
}
