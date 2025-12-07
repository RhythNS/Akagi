using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands;

internal abstract class Command
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract bool ContinueAfterExecution { get; }
    public Argument[] Arguments { get; set; } = [];
    public Message.Type From { get; set; } = 0;

    public abstract Argument[] GetDefaultArguments();

    public abstract Task Execute(Context context);

    protected CommandMessage CreateCommandMessage(string output)
    {
        return new CommandMessage
        {
            Command = this,
            Output = output,
            From = From,
            Time = DateTime.UtcNow
        };
    }
}
