using Akagi.Data;
using Akagi.Users;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Users;

internal class NullUserPreset : Preset
{
    private string _userId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        User user = new()
        {
            Name = "Null User",
            Username = "null_user",
            Valid = false,
            Admin = false,
            Dirty = true,
        };

        await Save(databaseFactory, user, UserId);

        UserId = user.Id!;
    }
}
