using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class ChangeUsernameCommand : TextCommand
{
    public override string Name => "/changeUserName";

    public override string Description => "Changes your username. Usage: /changeUserName <new name>";

    private readonly IUserDatabase _userDatabase;

    public ChangeUsernameCommand(IUserDatabase userDatabase)
    {
        _userDatabase = userDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a new username.");
            return;
        }
        string newUsername = string.Join(" ", args);
        if (newUsername.Length < 3 || newUsername.Length > 20)
        {
            await Communicator.SendMessage(context.User, "Username must be between 3 and 20 characters long.");
            return;
        }
        if (!newUsername.All(char.IsLetterOrDigit))
        {
            await Communicator.SendMessage(context.User, "Username can only contain letters and digits.");
            return;
        }
        if (await _userDatabase.GetByUsername(newUsername) != null)
        {
            await Communicator.SendMessage(context.User, "This username is already taken. Please choose another one.");
            return;
        }
        context.User.Username = newUsername;
        try
        {
            await _userDatabase.SaveDocumentAsync(context.User);
        }
        catch (Exception)
        {
            await Communicator.SendMessage( context.User, "Failed to change your username. Please try again later.");
            return;
        }
        await Communicator.SendMessage(context.User, $"Your username has been changed to {newUsername}.");
    }
}
