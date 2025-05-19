using Akagi.Characters;

namespace Akagi.Puppeteers.Commands.Messages;

internal abstract class MessageCommand : Command
{
    public override bool ContinueAfterExecution => false;

    public abstract Message GetMessage();
}
