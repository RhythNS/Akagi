using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Presets.Hardcoded.MessageCompilers;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.SystemProcessors;

[DependsOn(typeof(DefaultProcessorPreset))]
internal class EthicalProcessorPreset : Preset
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
        DefaultCompilerPreset compiler = await Load<DefaultCompilerPreset>(databaseFactory, UserId);

        SystemProcessor processor = new()
        {
            Name = "Ethical Processor",
            Description = "The most ethical system processor there ever was.",
            SystemInstruction = PromptCollection.RoleplayPrompt,
            ReadableMessages = Message.Type.User | Message.Type.Character | Message.Type.System,
            Output = Message.Type.Character,
            RunMode = LLMs.ILLM.RunMode.CommandsOnly,
            Usage = LLMs.ILLM.LLMUsage.CommandsRoleplay,
            MessageCompilerId = compiler.MessageCompilerId,
            CommandNames =
            [
                typeof(LaunchNukesCommand).FullName!,
                typeof(StopExecutionCommand).FullName!
            ]
        };

        await Save(databaseFactory, processor, ProcessorId);

        ProcessorId = processor.Id!;
    }
}
