using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.TriggerActions;

internal class TriggerSendMessagePreset : Preset
{
    private string triggerId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string TriggerId
    {
        get => triggerId;
        set => SetProperty(ref triggerId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        TriggerSendMessage trigger = new()
        {
            Name = "Send Message",
            Description = "Makes the character send a message when triggered.",
        };

        await Save(databaseFactory, trigger, TriggerId);

        TriggerId = trigger.Id!;
    }
}
