
namespace Akagi.Communication.Commands.ActiveCharacters;

internal class RenameCharacterCommand : TextCommand
{
    public override string Name => "/renameCharacter";

    public override string Description => "Renames the active character. Usage: /renameCharacter [new name]";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            return Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
        }

        if (args.Length < 1)
        {
            return Communicator.SendMessage(context.User, "Please provide a new name for the character.");
        }

        string newName = string.Join(" ", args);
        context.Character.Name = newName;

        return Communicator.SendMessage(context.User, $"Character renamed to '{newName}'.");
    }
}
