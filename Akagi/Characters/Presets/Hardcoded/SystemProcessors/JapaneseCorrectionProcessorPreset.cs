using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Presets.Hardcoded.MessageCompilers;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.SystemProcessors;

[DependsOn(typeof(Forgetful5MessageCompilerPreset))]
internal class JapaneseCorrectionProcessorPreset : Preset
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
        Forgetful5MessageCompilerPreset messageCompiler = await Load<Forgetful5MessageCompilerPreset>(databaseFactory);

        SystemProcessor processor = new()
        {
            Name = "Japanese Correction Processor",
            Description = "A system processor that corrects and refines Japanese text to ensure proper grammar, natural phrasing, and cultural appropriateness.",
            SystemInstruction = PromptCollection.JapaneseCorrectionPrompt,
            ReadableMessages = Message.Type.User | Message.Type.Character,
            Output = Message.Type.Character,
            RunMode = ILLM.RunMode.TextOnly,
            MessageCompilerId = messageCompiler.Id!,
            CommandNames = []
        };

        await Save(databaseFactory, processor, ProcessorId);

        ProcessorId = processor.Id!;
    }
}
