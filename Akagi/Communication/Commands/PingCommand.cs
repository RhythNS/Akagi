namespace Akagi.Communication.Commands;

internal class PingCommand : TextCommand
{
    public override string Name => "/ping";

    public override string Description => "Responds with 'Pong!' to verify that the bot is online. Usage: /ping";

    public override bool AdminOnly => true;

    public override async Task ExecuteAsync(Context context, string[] _)
    {
        await Communicator.SendMessage(context.User, "Pong!");
    }
}
