namespace Akagi.Receivers.Commands;

internal class StopExecutionCommand : Command
{
    public override string Name => "StopExecution";

    public override string Description => "Stops without doing anything.";

    public override Argument[] GetDefaultArguments() => [];

    public override bool ContinueAfterExecution => false;

    public override Task Execute(Context context)
    {
        string output = "Stopped execution!";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.CompletedTask;
    }
}
