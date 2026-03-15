
namespace Akagi.Communication.Commands.ActiveCharacters;

internal class RenameCharacterCommand : TextCommand
{
    public override string Name => "/renameCharacter";

    public override string Description => "Renames the active character. Usage: /renameCharacter [new name]";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }

        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a new name for the character.");
            return CommandResult.Fail("No name provided.");
        }

        string newName = string.Join(" ", args);
        context.Character.Name = newName;

        await Communicator.SendMessage(context.User, $"Character renamed to '{newName}'.");
        return CommandResult.Ok;
    }
}
