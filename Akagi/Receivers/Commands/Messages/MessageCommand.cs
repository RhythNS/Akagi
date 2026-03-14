using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands.Messages;

internal abstract class MessageCommand : Command
{
    public override bool ContinueAfterExecution => false;

    protected Message? _message;

    public Message GetMessage()
    {
        return _message ?? throw new InvalidOperationException("Message not set.");
    }
}
