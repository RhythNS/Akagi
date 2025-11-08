using Akagi.Receivers.Commands;

namespace Akagi.Characters.Conversations;

internal class CommandMessage : Message
{
    public required Command Command { get; set; }

    public override Message Copy()
    {
        return new CommandMessage
        {
            Time = Time,
            From = From,
            VisibleTo = VisibleTo,
            Command = Command
        };
    }
}
