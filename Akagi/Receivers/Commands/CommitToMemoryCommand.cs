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
        },
        new Argument
        {
            Name = "Target",
            Description = "Where to store the memory: 'character' (default), 'group' (shared group memory), or a specific character ID.",
            IsRequired = false,
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

        string target = Arguments.Length > 2 && !string.IsNullOrWhiteSpace(Arguments[2].Value)
            ? Arguments[2].Value
            : "character";

        Memory targetMemory = ResolveTargetMemory(context, target);

        ThoughtCollection<SingleFactThought> thoughtCollection = isLongTerm.Value ?
            targetMemory.LongTerm :
            targetMemory.ShortTerm;

        thoughtCollection.AddThought(singleFactThought);

        string output = $"Committed thought to {(isLongTerm.Value ? "long-term" : "short-term")} memory (target: {target}): \"{thought}\"";
        context.Conversation.AddMessage(CreateCommandMessage(output));

        return Task.FromResult(Array.Empty<Command>());
    }

    private static Memory ResolveTargetMemory(Context context, string target)
    {
        if (string.Equals(target, "group", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Group == null)
            {
                throw new InvalidOperationException("Cannot target 'group' memory when not in a group context.");
            }
            return context.Group.SharedMemory;
        }

        if (string.Equals(target, "character", StringComparison.OrdinalIgnoreCase))
        {
            return context.Character.Memory;
        }

        // Assume it's a character ID — not supported for now outside group context
        return context.Character.Memory;
    }
}
