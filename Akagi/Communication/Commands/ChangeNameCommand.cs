using Akagi.Users;

namespace Akagi.Communication.Commands
{
    internal class ChangeNameCommand : TextCommand
    {
        public override string Name => "/changeName";

        private readonly IUserDatabase _userDatabase;

        public ChangeNameCommand(IUserDatabase userDatabase)
        {
            _userDatabase = userDatabase;
        }

        public override async Task ExecuteAsync(User user, string[] args)
        {
            if (args.Length < 1)
            {
                await Communicator.SendMessage(user, "Please provide a new name.");
                return;
            }
            string newName = string.Join(" ", args);
            if (newName.Length < 3 || newName.Length > 20)
            {
                await Communicator.SendMessage(user, "Name must be between 3 and 20 characters long.");
                return;
            }
            user.Name = newName;
            try
            {
                await _userDatabase.SaveDocumentAsync(user);
            }
            catch (Exception)
            {
                await Communicator.SendMessage(user, "Failed to change your name. Please try again later.");
                return;
            }
            await Communicator.SendMessage(user, $"Your name has been changed to {newName}.");
        }
    }
}
