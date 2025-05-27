using Akagi.Users;

namespace Akagi.Communication.Commands;

internal class PingCommand : TextCommand
{
    public override string Name => "/ping";
    public override async Task ExecuteAsync(Context context, string[] _)
    {
        await Communicator.SendMessage(context.User, "Pong!");
    }
}
