using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets;

internal abstract class Preset : Savable
{
    private string _userId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public Task CreateAsync(IDatabaseFactory databaseFactory, string userId)
    {
        UserId = userId;
        return CreateInnerAsync(databaseFactory);
    }

    protected abstract Task CreateInnerAsync(IDatabaseFactory databaseFactory);

    protected static async Task<T> Load<T>(IDatabaseFactory databaseFactory, string userId) where T : Preset
    {
        return await databaseFactory.GetDatabase<IPresetDatabase>().GetPreset<T>(userId)
            ?? throw new InvalidOperationException($"{typeof(T).Name} not found");
    }

    protected static async Task Save(IDatabaseFactory databaseFactory, Savable savable, string? overwriteId = null)
    {
        if (string.IsNullOrEmpty(overwriteId) == false)
        {
            savable.Id = overwriteId;
        }

        bool success = await databaseFactory.TrySave(savable);

        if (!success)
        {
            throw new InvalidOperationException("Failed to save JapaneseCorrectionRoleplayPuppeteerPreset");
        }
    }
}
