using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.Presets.Hardcoded.SystemProcessors;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Reflectors;

[DependsOn(typeof(ConversationSummaryReflectionProcessorPreset))]
internal class DefaultReflectorPreset : Preset
{
    private string reflectorId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string ReflectorId
    {
        get => reflectorId;
        set => SetProperty(ref reflectorId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        ConversationSummaryReflectionProcessorPreset preset = await Load<ConversationSummaryReflectionProcessorPreset>(databaseFactory, UserId);

        DefaultReflector reflector = new()
        {
            Name = "Default Reflector",
            ConversationSystemProccessorId = preset.ProcessorId
        };

        await Save(databaseFactory, reflector, ReflectorId);

        ReflectorId = reflector.Id!;
    }
}
