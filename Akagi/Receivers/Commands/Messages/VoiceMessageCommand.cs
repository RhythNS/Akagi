using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands.Messages;

internal class VoiceMessageCommand : MessageCommand
{
    public override string Name => "VoiceMessage";

    public override string Description => "Command to save a voice message.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Content",
            Description = "The transcribed content of the voice message.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "MessageId",
            Description = "The ID of the voice message.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
    ];

    public override Task<Command[]> Execute(Context context)
    {
        string content = Arguments[0].Value;
        string messageId = Arguments[1].Value;

        _message = new VoiceMessage
        {
            Content = content,
            VoiceId = messageId,
            Time = DateTime.UtcNow,
            From = From
        };
        context.Conversation.AddMessage(_message);
        return Task.FromResult(Array.Empty<Command>());
    }

    public void SetMessage(string content, string messageId)
    {
        Arguments = GetDefaultArguments();
        Arguments[0].Value = content;
        Arguments[1].Value = messageId;
    }
}
