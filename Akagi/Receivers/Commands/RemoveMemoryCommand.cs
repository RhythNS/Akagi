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
        },
        new Argument
        {
            Name = "Target",
            Description = "Where to remove the memory from: 'character' (default), 'group' (shared group memory), or a specific character ID.",
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

        string target = Arguments.Length > 2 && !string.IsNullOrWhiteSpace(Arguments[2].Value)
            ? Arguments[2].Value
            : "character";

        Memory targetMemory = ResolveTargetMemory(context, target);

        ThoughtCollection<SingleFactThought> thoughtCollection = isLongTerm.Value ?
            targetMemory.LongTerm :
            targetMemory.ShortTerm;

        if (index < 0 || index >= thoughtCollection.Thoughts.Count)
        {
            throw new ArgumentOutOfRangeException($"MemoryID {index} is out of range. Valid range is 0 to {thoughtCollection.Thoughts.Count - 1}.");
        }

        SingleFactThought deletedThought = thoughtCollection.Thoughts[index.Value];
        thoughtCollection.RemoveThoughtAt(index.Value);

        string output = $"Removed {(isLongTerm.Value ? "long-term" : "short-term")} memory with ID {index} (target: {target}). Thought: \"{deletedThought.Fact}\".";
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

        return context.Character.Memory;
    }
}
