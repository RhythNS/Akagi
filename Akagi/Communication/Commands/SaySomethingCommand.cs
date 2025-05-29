using Akagi.Receivers;

namespace Akagi.Communication.Commands;

internal class SaySomethingCommand : TextCommand
{
    public override string Name => "/saySomething";

    public override string Description => "Triggers the receiver to continue the conversation. Usage: /saySomething";

    private readonly IReceiver _receiver;

    public SaySomethingCommand(IReceiver receiver)
    {
        _receiver = receiver;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        return _receiver.OnPromptContinue(Communicator, context.Character, context.User);
    }
}
