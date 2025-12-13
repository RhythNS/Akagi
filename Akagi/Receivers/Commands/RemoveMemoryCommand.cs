using Akagi.Characters.Memories;

namespace Akagi.Receivers.Commands;

internal class RemoveMemoryCommand : Command
{
    public override string Name => "RemoveMemory";

    public override string Description => "Remove an existing memory.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "MemoryID",
            Description = "The ID of the memory to remove.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "IsLongTerm",
            Description = "Whether the memory is long-term (true) or short-term (false).",
            IsRequired = true,
            ArgumentType = Argument.Type.Bool
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task Execute(Context context)
    {
        if (Arguments.Length < 2 ||
            string.IsNullOrWhiteSpace(Arguments[0].Value) ||
            string.IsNullOrWhiteSpace(Arguments[1].Value))
        {
            throw new ArgumentException("MemoryID and IsLongTerm arguments are required and cannot be empty.");
        }
        int? index = Arguments[0].IntValue;
        if (index == null)
        {
            throw new ArgumentException($"MemoryID argument must be a valid integer. Received: {Arguments[0].Value}");
        }
        bool? isLongTerm = Arguments[1].BoolValue;
        if (isLongTerm == null)
        {
            throw new ArgumentException($"IsLongTerm argument must be a valid boolean. Received: {Arguments[1].Value}");
        }

        ThoughtCollection<SingleFactThought> thoughtCollection = isLongTerm.Value ?
            context.Character.Memory.LongTerm :
            context.Character.Memory.ShortTerm;

        if (index < 0 || index >= thoughtCollection.Thoughts.Count)
        {
            throw new ArgumentOutOfRangeException($"MemoryID {index} is out of range. Valid range is 0 to {thoughtCollection.Thoughts.Count - 1}.");
        }

        SingleFactThought deletedThought = thoughtCollection.Thoughts[index.Value];
        thoughtCollection.RemoveThoughtAt(index.Value);

        string output = $"Removed {(isLongTerm.Value ? "long-term" : "short-term")} memory with ID {index}. Thought: \"{deletedThought.Fact}\".";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.CompletedTask;
    }
}
