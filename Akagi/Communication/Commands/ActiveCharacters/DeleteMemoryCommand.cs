
namespace Akagi.Communication.Commands.ActiveCharacters;

internal class DeleteMemoryCommand : TextCommand
{
    public override string Name => "/deleteMemory";

    public override string Description => "Deletes the entire memory of the active character. Usage: /deleteMemory <safteyConfirm>";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            return Communicator.SendMessage(context.User, "You must select a character to use this command.");
        }
        if (args.Length < 1 || string.Equals(args[0], "confirm", StringComparison.OrdinalIgnoreCase) == false)
        {
            return Communicator.SendMessage(context.User, "You must type '/deleteMemory confirm' to confirm deletion of the character's memory.");
        }

        context.Character.Memory.Clear();

        return Communicator.SendMessage(context.User, "The entire memory of the active character has been deleted.");
    }
}
