using Akagi.Characters;
using Akagi.Characters.Conversations;

namespace Akagi.Receivers.Commands.Messages;

internal class TextMessageCommand : MessageCommand
{
    public override string Name => "TextMessageCommand";

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
        }
    ];

    private readonly ICharacterDatabase _characterDatabase;
    private Message? _message;

    public TextMessageCommand(ICharacterDatabase characterDatabase)
    {
        _characterDatabase = characterDatabase;
    }

    public override Task Execute(Context context)
    {
        string text = Arguments[0].Value;
        _message = context.Conversation.AddMessage(text, DateTime.UtcNow, Message.Type.User);
        return _characterDatabase.SaveDocumentAsync(context.Character);
    }

    public void SetMessage(string message)
    {
        Arguments[0].Value = message;
    }

    public override Message GetMessage()
    {
        return _message ?? throw new InvalidOperationException("Message not set.");
    }
}
