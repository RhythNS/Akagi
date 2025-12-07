using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Reflectors;

internal class NullReflectorPreset : Preset
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
        NullReflector reflector = new()
        {
            Name = "Null Reflector",
        };

        await Save(databaseFactory, reflector, ReflectorId);

        ReflectorId = reflector.Id!;
    }
}
