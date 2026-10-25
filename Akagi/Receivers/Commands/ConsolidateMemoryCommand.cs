using Akagi.Characters.Memories;

namespace Akagi.Receivers.Commands;

internal class ConsolidateMemoryCommand : Command
{
    public override string Name => "ConsolidateMemory";

    public override string Description => "Extracts and consolidates a permanent factual memory learned about the user or world, with optional contradiction archiving.";

    public override Argument[] GetDefaultArguments() =>
    [
        new Argument
        {
            Name = "Fact",
            Description = "The concise factual statement to remember.",
            IsRequired = true,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "Importance",
            Description = "Importance score from 1 (minor detail) to 10 (crucial life milestone or core truth). Default is 5.0.",
            IsRequired = false,
            ArgumentType = Argument.Type.Float
        },
        new Argument
        {
            Name = "Category",
            Description = "The category of the memory (e.g. Career, Preference, Relationship, Health, Hobby).",
            IsRequired = false,
            ArgumentType = Argument.Type.String
        },
        new Argument
        {
            Name = "SupersedesMemoryId",
            Description = "If this fact contradicts or updates an existing memory, provide its ID so the outdated fact is archived.",
            IsRequired = false,
            ArgumentType = Argument.Type.String
        }
    ];

    public override bool ContinueAfterExecution => true;

    public override async Task<Command[]> Execute(Context context)
    {
        if (Arguments.Length == 0 || string.IsNullOrWhiteSpace(Arguments[0].Value))
        {
            throw new ArgumentException("The 'Fact' argument is required and cannot be empty.");
        }

        string fact = Arguments[0].Value.Trim();

        float importance = Arguments.Length > 1 && Arguments[1].FloatValue is float impVal
            ? Math.Clamp(impVal, 1.0f, 10.0f)
            : 5.0f;

        string? category = Arguments.Length > 2 && !string.IsNullOrWhiteSpace(Arguments[2].Value)
            ? Arguments[2].Value.Trim()
            : null;

        string? supersedesId = Arguments.Length > 3 && !string.IsNullOrWhiteSpace(Arguments[3].Value)
            ? Arguments[3].Value.Trim()
            : null;

        IMemoryDatabase memoryDb = context.DatabaseFactory.GetDatabase<IMemoryDatabase>();

        // If an older contradictory or superseded memory ID was provided, archive it
        if (!string.IsNullOrWhiteSpace(supersedesId))
        {
            await memoryDb.ArchiveMemoryAsync(supersedesId);
        }

        // Generate vector embedding for semantic retrieval
        float[] embedding = [];
        if (context.EmbeddingService != null)
        {
            try
            {
                embedding = await context.EmbeddingService.GenerateEmbeddingAsync(fact);
            }
            catch
            {
                // Continue without embedding if provider is temporarily unreachable
            }
        }

        MemoryRecord record = new()
        {
            CharacterId = context.Character.Id!,
            UserId = context.User?.Id,
            Fact = fact,
            Embedding = embedding,
            Importance = importance,
            Category = category,
            Timestamp = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            IsArchived = false
        };

        await memoryDb.SaveDocumentAsync(record);

        string output = $"[Consolidated Memory] (Imp: {importance:F1}, Cat: {category ?? "None"}): \"{fact}\"" +
                        (supersedesId != null ? $" (Archived superseded memory: {supersedesId})" : string.Empty);

        context.Conversation.AddMessage(CreateCommandMessage(output));

        return [];
    }
}
