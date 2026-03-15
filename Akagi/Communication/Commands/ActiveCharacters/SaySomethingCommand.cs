using Akagi.Receivers;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class SaySomethingCommand : TextCommand
{
    public override string Name => "/saySomething";

    public override string Description => "Triggers the receiver to continue the conversation. Usage: /saySomething";

    private readonly IReceiver _receiver;

    public SaySomethingCommand(IReceiver receiver)
    {
        _receiver = receiver;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }
        await _receiver.OnPromptContinue(Communicator, context.Character, context.User);
        return CommandResult.Ok;
    }
}
