using Akagi.Users;

namespace Akagi.Communication.Commands
{
    internal class ChangeUsernameCommand : TextCommand
    {
        public override string Name => "/changeUserName";

        private readonly IUserDatabase _userDatabase;

        public ChangeUsernameCommand(IUserDatabase userDatabase)
        {
            _userDatabase = userDatabase;
        }

        public override async Task ExecuteAsync(User user, string[] args)
        {
            if (args.Length < 1)
            {
                await Communicator.SendMessage(user, "Please provide a new username.");
                return;
            }
            string newUsername = string.Join(" ", args);
            if (newUsername.Length < 3 || newUsername.Length > 20)
            {
                await Communicator.SendMessage(user, "Username must be between 3 and 20 characters long.");
                return;
            }
            if (!newUsername.All(char.IsLetterOrDigit))
            {
                await Communicator.SendMessage(user, "Username can only contain letters and digits.");
                return;
            }
            if (await _userDatabase.GetByUsername(newUsername) != null)
            {
                await Communicator.SendMessage(user, "This username is already taken. Please choose another one.");
                return;
            }
            user.Username = newUsername;
            try
            {
                await _userDatabase.SaveDocumentAsync(user);
            }
            catch (Exception)
            {
                await Communicator.SendMessage(user, "Failed to change your username. Please try again later.");
                return;
            }
            await Communicator.SendMessage(user, $"Your username has been changed to {newUsername}.");
        }
    }
}
