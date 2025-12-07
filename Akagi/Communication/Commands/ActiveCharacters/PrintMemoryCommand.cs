using System.Text.Json;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class PrintMemoryCommand : TextCommand
{
    public override string Name => "/printMemoryCommand";

    public override string Description => "Prints the entire memory of the active character.";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            return Communicator.SendMessage(context.User, "You must select a character to use this command.");
        }

        string memory = JsonSerializer.Serialize(context.Character.Memory);
        return Communicator.SendMessage(context.User, memory);
    }
}
