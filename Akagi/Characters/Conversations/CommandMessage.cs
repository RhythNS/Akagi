using Akagi.Receivers.Commands;

namespace Akagi.Characters.Conversations;

internal class CommandMessage : Message
{
    public required Command Command { get; set; }

    public override bool IsVisible => false;
}
