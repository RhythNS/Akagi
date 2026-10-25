using Akagi.Bridge.Attributes;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Memories;
using Akagi.Flow;
using Akagi.LLMs;
using Akagi.Receivers.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using System.Text;

namespace Akagi.Characters.CharacterBehaviors.Reflectors;

[GraphNode]
internal class MemoryConsolidationReflector : Reflector
{
    private string _systemProcessorId = string.Empty;
    private int _minMessagesToConsolidate = 4;
    private float _minImportanceToSave = 3.0f;
    private ILLM.LLMUsage _usage = ILLM.LLMUsage.ReflectionRoleplay;

    [NodeReference(typeof(SystemProcessor))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string SystemProcessorId
    {
        get => _systemProcessorId;
        set => SetProperty(ref _systemProcessorId, value);
    }

    public int MinMessagesToConsolidate
    {
        get => _minMessagesToConsolidate;
        set => SetProperty(ref _minMessagesToConsolidate, value);
    }

    public float MinImportanceToSave
    {
        get => _minImportanceToSave;
        set => SetProperty(ref _minImportanceToSave, value);
    }

    public ILLM.LLMUsage Usage
    {
        get => _usage;
        set => SetProperty(ref _usage, value);
    }

    private SystemProcessor? _loadedProcessor;

    protected override async Task InnerInit()
    {
        if (!string.IsNullOrWhiteSpace(SystemProcessorId))
        {
            _loadedProcessor = await SystemProcessorDatabase.GetSystemProcessor(SystemProcessorId);
        }
    }

    public override async Task ProcessAsync()
    {
        List<Conversation> conversations = GetConversationsToConsolidate();
        if (conversations.Count == 0)
        {
            Logger.LogDebug("No unreflected conversations found for character {CharacterId}.", Character.Id);
            return;
        }

        Logger.LogInformation("Starting memory consolidation for {Count} conversations on character {CharacterId}.",
            conversations.Count, Character.Id);

        foreach (Conversation conversation in conversations)
        {
            await ConsolidateConversationAsync(conversation);
        }
    }

    protected virtual List<Conversation> GetConversationsToConsolidate()
    {
        return [.. Character.Conversations.List.Where(c =>
            c.Messages.Count >= MinMessagesToConsolidate &&
            Character.Memory.Conversations.Thoughts.FirstOrDefault(t => t.ConversationId == c.Id) == null)];
    }

    private async Task ConsolidateConversationAsync(Conversation conversation)
    {
        Context.Conversation = conversation;

        IMemoryDatabase memoryDb = Context.DatabaseFactory.GetDatabase<IMemoryDatabase>();
        List<MemoryRecord> activeMemories = await memoryDb.GetActiveMemoriesAsync(
            Character.Id!,
            User?.Id,
            includeGlobalLore: false);

        StringBuilder memSb = new();
        if (activeMemories.Count > 0)
        {
            memSb.AppendLine("Existing memories about this user:");
            foreach (MemoryRecord m in activeMemories)
            {
                memSb.AppendLine($"[ID: {m.Id}] (Imp: {m.Importance:F1}) {m.Fact}");
            }
        }
        else
        {
            memSb.AppendLine("No existing memories stored yet.");
        }

        SystemProcessor processor;
        if (_loadedProcessor != null)
        {
            processor = _loadedProcessor;
        }
        else
        {
            ICommandFactory commandFactory = Globals.Instance.ServiceProvider.GetRequiredService<ICommandFactory>();
            ConsolidateMemoryCommand consolidateCmd = commandFactory.Create<ConsolidateMemoryCommand>();

            processor = new SystemProcessor
            {
                Name = "MemoryConsolidationProcessor",
                Description = "Extracts permanent facts from dialogue with conflict resolution",
                RunMode = ILLM.RunMode.CommandsOnly,
                Usage = Usage,
                ReadableMessages = Message.Type.Character | Message.Type.User,
                Output = Message.Type.System,
                MessageCompiler = new DefaultMessageCompiler(),
                SystemInstruction =
                    "You are the Memory Consolidation Engine for an AI companion.\n" +
                    "Review the conversation dialogue and extract discrete, important factual statements learned about the user.\n" +
                    "Call the 'ConsolidateMemory' tool for each new fact.\n" +
                    "Focus on real-world details: occupation, location, family, personal preferences, hobbies, goals, milestones, and relationship changes.\n" +
                    "Do NOT record temporary small talk, greetings, jokes, or fleeting dialogue.\n" +
                    $"Only record facts with importance >= {MinImportanceToSave:F1} (scale 1 to 10).\n" +
                    "If an extracted fact contradicts, supersedes, or updates an existing memory from the list below, pass that memory's ID in 'SupersedesMemoryId' to archive the outdated fact.\n\n" +
                    memSb.ToString()
            };

            await processor.Init([consolidateCmd], Context.DatabaseFactory);
        }

        ILLM llm = await LLMFactory.Create(Context.User, processor.SpecificLLM, processor.Usage);

        await DefaultNextSteps(llm, processor);

        // Record a conversation thought to track that this conversation has been consolidated
        if (Character.Memory.Conversations.Thoughts.FirstOrDefault(t => t.ConversationId == conversation.Id) == null)
        {
            Character.Memory.Conversations.AddThought(new ConversationThought
            {
                ConversationId = conversation.Id,
                ShortSummary = "Consolidated",
                LongSummary = "Memories consolidated by MemoryConsolidationReflector",
                Timestamp = DateTime.UtcNow
            });
            Character.Dirty = true;
        }

        Logger.LogInformation("Memory consolidation completed for conversation {ConversationId} on character {CharacterId}.",
            conversation.Id, Character.Id);
    }
}
