using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.TriggerActions;

internal class TriggerReflectPreset : Preset
{
    private string _triggerId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string TriggerId
    {
        get => _triggerId;
        set => SetProperty(ref _triggerId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        TriggerReflect triggerReflect = new()
        {
            Name = "Reflect Action",
            Description = "Triggers the character to reflect.",
        };

        await Save(databaseFactory, triggerReflect, TriggerId);

        TriggerId = triggerReflect.Id!;
    }
}
