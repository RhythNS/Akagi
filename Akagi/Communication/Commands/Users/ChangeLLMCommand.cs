using Akagi.LLMs;
using Microsoft.Extensions.Options;

namespace Akagi.Communication.Commands.Users;

internal class ChangeLLMCommand : TextCommand
{
    public override string Name => "/changeLLM";

    public override string Description => "Changes your preferred LLM. Usage: /changeLLM <LLM index>";

    private readonly LLMDefinitions _llmDefinitions;

    public ChangeLLMCommand(IOptions<LLMDefinitions> llmDefinitions)
    {
        _llmDefinitions = llmDefinitions.Value;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            return Communicator.SendMessage(context.User, "Please provide the LLM index to switch to.");
        }
        if (!int.TryParse(args[0], out int llmIndex))
        {
            return Communicator.SendMessage(context.User, "Invalid LLM index. Please provide a valid number.");
        }
        if (llmIndex < 0 || llmIndex >= _llmDefinitions.Definitions.Length)
        {
            return Communicator.SendMessage(context.User, $"LLM index out of range. Please provide a number between 0 and {_llmDefinitions.Definitions.Length - 1}.");
        }

        LLMDefinition llmDefinition = _llmDefinitions.Definitions[llmIndex];
        context.User.LLMDefinition = llmDefinition;
        return Communicator.SendMessage(context.User, $"Your preferred LLM has been changed to {llmDefinition.Type}:{llmDefinition.Model}.");
    }
}
