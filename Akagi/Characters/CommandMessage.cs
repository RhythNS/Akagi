using Akagi.Puppeteers.Commands;

namespace Akagi.Characters;

internal class CommandMessage : Message
{
    public Command Command { get; set; }

    public override bool IsVisible => false;
}
