using Akagi.Puppeteers.Commands;

namespace Akagi.Characters.Conversations;

internal class CommandMessage : Message
{
    public Command Command { get; set; }

    public override bool IsVisible => false;
}
