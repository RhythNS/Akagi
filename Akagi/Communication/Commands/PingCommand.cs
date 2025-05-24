using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class PingCommand : TextCommand
{
    public override string Name => "/ping";
    public override async Task ExecuteAsync(User user, string[] _)
    {
        await Communicator.SendMessage(user, "Pong!");
    }
}
