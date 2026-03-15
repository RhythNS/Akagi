using System.Text.Json;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class PrintMemoryCommand : TextCommand
{
    public override string Name => "/printMemory";

    public override string Description => "Prints the entire memory of the active character.";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You must select a character to use this command.");
            return CommandResult.Fail("No active character.");
        }

        string memory = JsonSerializer.Serialize(context.Character.Memory);
        await Communicator.SendMessage(context.User, memory);
        return CommandResult.Ok;
    }
}
