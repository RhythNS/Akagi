namespace Akagi.Communication.Commands;

internal class ChangeNameCommand : TextCommand
{
    public override string Name => "/changeName";

    public override string Description => "Changes your name. Usage: /changeName <new name>";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a new name.");
            return;
        }
        string newName = string.Join(" ", args);
        if (newName.Length < 3 || newName.Length > 20)
        {
            await Communicator.SendMessage(context.User, "Name must be between 3 and 20 characters long.");
            return;
        }
        context.User.Name = newName;

        await Communicator.SendMessage(context.User, $"Your name has been changed to {newName}.");
    }
}
