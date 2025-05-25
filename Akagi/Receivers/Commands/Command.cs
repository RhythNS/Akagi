namespace Akagi.Receivers.Commands;

internal abstract class Command
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Argument[] Arguments { get; }

    public abstract bool ContinueAfterExecution { get; }
    public abstract Task Execute(Context context);
}
