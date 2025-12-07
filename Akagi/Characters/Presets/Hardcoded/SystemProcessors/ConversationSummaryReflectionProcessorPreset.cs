using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Presets.Hardcoded.MessageCompilers;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.SystemProcessors;

[DependsOn(typeof(LatestCompletedConversationCompilerPreset))]
internal class ConversationSummaryReflectionProcessorPreset : Preset
{
    private string _processorId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ProcessorId
    {
        get => _processorId;
        set => SetProperty(ref _processorId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        LatestCompletedConversationCompilerPreset compiler = await Load<LatestCompletedConversationCompilerPreset>(databaseFactory, UserId);

        SystemProcessor processor = new()
        {
            Name = "Conversation Summary Reflection Processor",
            Description = "Summarizes and reflects on the latest completed conversation.",
            SystemInstruction = PromptCollection.ReflectionPrompt,
            ReadableMessages = Message.Type.User | Message.Type.Character,
            Output = Message.Type.System,
            RunMode = LLMs.ILLM.RunMode.CommandsOnly,
            MessageCompilerId = compiler.MessageCompilerId,
            CommandNames = [typeof(SummarizeConversationCommand).FullName!]
        };

        await Save(databaseFactory, processor, ProcessorId);

        ProcessorId = processor.Id!;
    }
}
