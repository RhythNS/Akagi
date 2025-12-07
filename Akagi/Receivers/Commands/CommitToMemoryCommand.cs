using Akagi.Characters.Memories;

namespace Akagi.Receivers.Commands;

internal class CommitToMemoryCommand : Command
{
    public override string Name => "CommitToMemory";

    public override string Description => "Commit a thought to memory.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Thought",
            Description = "The thought to commit to memory.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task Execute(Context context)
    {
        if (Arguments.Length < 1 || string.IsNullOrWhiteSpace(Arguments[0].Value))
        {
            throw new ArgumentException("Thought argument is required and cannot be empty.");
        }
        string thought = Arguments[0].Value;
        SingleFactThought singleFactThought = new()
        {
            Fact = thought,
            Timestamp = DateTime.UtcNow
        };
        context.Character.Memory.ShortTerm.AddThought(singleFactThought);

        string output = $"Committed thought to memory: \"{thought}\"";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.CompletedTask;
    }
}
