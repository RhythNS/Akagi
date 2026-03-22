using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands;

internal abstract class Command
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract bool ContinueAfterExecution { get; }
    public Argument[] Arguments { get; set; } = [];
    public Message.Type From { get; set; } = 0;
    public string? FromCharacterId { get; set; }

    public abstract Argument[] GetDefaultArguments();

    public abstract Task<Command[]> Execute(Context context);

    public void SetFrom(Message.Type from, string fromCharacterId)
    {
        From = from;
        FromCharacterId = fromCharacterId;
    }

    protected CommandMessage CreateCommandMessage(string output)
    {
        return new CommandMessage
        {
            Command = this,
            Output = output,
            From = From,
            CharacterId = FromCharacterId,
            Time = DateTime.UtcNow
        };
    }
}
