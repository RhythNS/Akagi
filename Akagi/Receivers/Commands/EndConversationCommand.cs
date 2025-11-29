
namespace Akagi.Receivers.Commands;

internal class EndConversationCommand : Command
{
    public override string Name => "EndConversation";

    public override string Description => "Ends the conversation on the specified message index.";

    public override Argument[] Arguments => _arguments;
    private readonly Argument[] _arguments = [
        new Argument
        {
            Name = "MessageIndex",
            Description = "The index of the last massage to include in the conversation before ending it.",
            ArgumentType = Argument.Type.Int,
            IsRequired = true
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task Execute(Context context)
    {
        if (Arguments.Length < 1)
        {
            throw new ArgumentException("MessageIndex argument is required.");
        }

        int? index = Arguments[0].IntValue;
        if (index == null)
        {
            throw new ArgumentException("MessageIndex argument must be an integer.");
        }

        context.Character.CompleteCurrentConversationAtIndex(index.Value);

        return Task.CompletedTask;
    }
}
