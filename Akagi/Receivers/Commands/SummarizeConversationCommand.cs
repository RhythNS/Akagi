using Akagi.Characters;
using Akagi.Characters.Memories;

namespace Akagi.Receivers.Commands;

internal class SummarizeConversationCommand : Command
{
    public override string Name => "SummarizeConversation";

    public override string Description => "Summarizes the conversation with a short consise summary and a slightly longer summary.";

    public override Argument[] Arguments => _arguments;
    private readonly Argument[] _arguments =
    [
        new Argument
        {
            Name = "conversationId",
            Description = "The ID of the conversation to summarize.",
            ArgumentType = Argument.Type.Int,
            IsRequired = true
        },
        new Argument
        {
            Name = "shortSummary",
            Description = "The short summary of the conversation.",
            ArgumentType = Argument.Type.String,
            IsRequired = true,
        },
        new Argument
        {
            Name = "longSummary",
            Description = "The long summary of the conversation.",
            ArgumentType = Argument.Type.String,
            IsRequired = true,
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override Task Execute(Context context)
    {
        if (Arguments.Length < 3)
        {
            throw new ArgumentException("Insufficient arguments provided.");
        }

        int? id = Arguments[0].IntValue;
        if (id == null)
        {
            throw new ArgumentException("Invalid conversation ID provided. It must be an integer.");
        }

        string? shortSummary = Arguments[1].Value;
        if (string.IsNullOrWhiteSpace(shortSummary))
        {
            throw new ArgumentException("Short summary cannot be null or empty.");
        }

        string? longSummary = Arguments[2].Value;
        if (string.IsNullOrWhiteSpace(longSummary))
        {
            throw new ArgumentException("Long summary cannot be null or empty.");
        }

        Conversation? conversation = context.Character.Conversations.FirstOrDefault(c => c.Id == id);
        if (conversation == null)
        {
            throw new ArgumentException($"Conversation with ID {id} not found.");
        }

        ConversationThought thought = new()
        {
            ConversationId = conversation.Id,
            ShortSummary = shortSummary,
            LongSummary = longSummary,
            Timestamp = DateTime.UtcNow
        };

        context.Character.Memory.Conversations.AddThought(thought);
        return Task.CompletedTask;
    }
}
