using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Presets.Hardcoded.MessageCompilers;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.SystemProcessors;

[DependsOn(typeof(DefaultCompilerPreset))]
internal class RoleplayProcessorPreset : Preset
{
    private string _processorId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ProcessorId
    {
        get => _processorId;
        set => SetProperty(ref _processorId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        // TODO: Replace with a more suitable compiler if needed
        DefaultCompilerPreset defaultCompiler = await Load<DefaultCompilerPreset>(databaseFactory);

        SystemProcessor processor = new()
        {
            Name = "Roleplay Processor",
            Description = "A system processor designed for roleplaying scenarios, enhancing character interactions and immersion.",
            SystemInstruction = PromptCollection.RoleplayPrompt,
            ReadableMessages = Message.Type.User | Message.Type.Character | Message.Type.System,
            Output = Message.Type.Character,
            RunMode = LLMs.ILLM.RunMode.Mixed,
            MessageCompilerId = defaultCompiler.MessageCompilerId,
            CommandNames = [typeof(RemindCommand).FullName!]
        };

        await Save(databaseFactory, processor, ProcessorId);

        ProcessorId = processor.Id!;
    }
}
