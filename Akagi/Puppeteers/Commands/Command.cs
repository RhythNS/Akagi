using Akagi.Characters;

namespace Akagi.Puppeteers.Commands;

internal abstract class Command
{
    public class Context
    {
        public required Character Character { get; init; }
        public required Conversation Conversation { get; init; }
    }

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract Argument[] Arguments { get; }

    public abstract bool ContinueAfterExecution { get; }
    public abstract Task Execute(Context context);
}
