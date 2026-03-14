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
            Name = "IsLongTerm",
            Description = "Whether the memory is long-term (true) or short-term (false).",
            IsRequired = true,
            ArgumentType = Argument.Type.Bool
        },
        new Argument
        {
            Name = "Thought",
            Description = "The thought to commit to memory.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task<Command[]> Execute(Context context)
    {
        if (Arguments.Length < 2 ||
            string.IsNullOrWhiteSpace(Arguments[0].Value) ||
            string.IsNullOrWhiteSpace(Arguments[1].Value))
        {
            throw new ArgumentException("IsLongTerm and Thought arguments are required and cannot be empty.");
        }
        bool? isLongTerm = Arguments[0].BoolValue;
        if (isLongTerm == null)
        {
            throw new ArgumentException("IsLongTerm argument must be a valid boolean value (true or false).");
        }

        string thought = Arguments[1].Value;
        SingleFactThought singleFactThought = new()
        {
            Fact = thought,
            Timestamp = DateTime.UtcNow
        };

        ThoughtCollection<SingleFactThought> thoughtCollection = isLongTerm.Value ?
            context.Character.Memory.LongTerm :
            context.Character.Memory.ShortTerm;

        thoughtCollection.AddThought(singleFactThought);

        string output = $"Committed thought to {(isLongTerm.Value ? "long-term" : "short-term")} memory: \"{thought}\"";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.FromResult(Array.Empty<Command>());
    }
}
