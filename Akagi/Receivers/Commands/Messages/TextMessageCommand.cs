using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands.Messages;

internal class TextMessageCommand : MessageCommand
{
    public override string Name => "TextMessage";

    public override string Description => "Command to save a text message.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Message",
            Description = "The message to save.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override Task<Command[]> Execute(Context context)
    {
        string text = Arguments[0].Value;
        _message = new TextMessage
        {
            Text = text,
            From = From,
            Time = DateTime.UtcNow,
            CharacterId = FromCharacterId
        };
        context.Conversation.AddMessage(_message);
        return Task.FromResult(Array.Empty<Command>());
    }

    public void SetMessage(string message)
    {
        Arguments = GetDefaultArguments();
        Arguments[0].Value = message;
    }
}
