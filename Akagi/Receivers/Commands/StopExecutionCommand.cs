namespace Akagi.Receivers.Commands;

internal class StopExecutionCommand : Command
{
    public override string Name => "StopExecution";

    public override string Description => "Stops the current conversation.";

    public override Argument[] Arguments => [];

    public override bool ContinueAfterExecution => false;

    public override Task Execute(Context context) => Task.CompletedTask;
}
