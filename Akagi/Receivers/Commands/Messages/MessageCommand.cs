using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands.Messages;

internal abstract class MessageCommand : Command
{
    public override bool ContinueAfterExecution => false;

    public abstract Message GetMessage();
}
