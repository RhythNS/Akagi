using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Presets;

internal interface IPresetDatabase : IDatabase<Preset>
{
    public Task<T?> GetPreset<T>(string userId) where T : Preset;
    public Task<List<Preset>> GetAllPresets(string userId);
}

internal class PresetDatabase : Database<Preset>, IPresetDatabase
{
    public PresetDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "presets")
    {
    }

    public async Task<T?> GetPreset<T>(string userId) where T : Preset
    {
        FilterDefinition<Preset> filter = Builders<Preset>.Filter.OfType<T>() &
            Builders<Preset>.Filter.Eq(p => p.UserId, userId);
        List<Preset> existing = await GetDocumentsByPredicateAsync(filter);

        if (existing.Count > 0)
        {
            return existing[0] as T;
        }

        return null;
    }

    public Task<List<Preset>> GetAllPresets(string userId)
    {
        FilterDefinition<Preset> filter = Builders<Preset>.Filter.Eq(p => p.UserId, userId);
        return GetDocumentsByPredicateAsync(filter);
    }

    public override bool CanSave(Savable savable)
    {
        return savable is Preset;
    }

    public override Task SaveAsync(Savable savable)
    {
        return SaveDocumentAsync((Preset)savable);
    }
}
