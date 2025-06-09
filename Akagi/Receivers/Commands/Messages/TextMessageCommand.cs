using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands.Messages;

internal class TextMessageCommand : MessageCommand
{
    public override string Name => "TextMessage";

    public override string Description => "Command to save a text message.";

    public override Argument[] Arguments => _arguments;
    private readonly Argument[] _arguments =
    [
        new Argument
        {
            Name = "Message",
            Description = "The message to save.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "From",
            Description = "Wheter the message was from the system or the character.",
            IsRequired = true,
            ArgumentType = Argument.Type.String,
        }
    ];

    private Message? _message;

    public override Task Execute(Context context)
    {
        string text = Arguments[0].Value;
        Message.Type type = Enum.TryParse(Arguments[1].Value, true, out Message.Type parsedType) ? parsedType : Message.Type.Character;
        _message = context.Conversation.AddMessage(text, DateTime.UtcNow, Message.Type.Character, type);
        return Task.CompletedTask;
    }

    public void SetMessage(string message, Message.Type type)
    {
        Arguments[0].Value = message;
        Arguments[1].Value = type.ToString();
    }

    public override Message GetMessage()
    {
        return _message ?? throw new InvalidOperationException("Message not set.");
    }
}
