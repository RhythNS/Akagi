using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Presets;

internal class PresetDatabase : Database<Preset>, IPresetDatabase
{
    public PresetDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "presets")
    {
    }

    public async Task<T?> GetPreset<T>() where T : Preset
    {
        FilterDefinition<Preset> filter = Builders<Preset>.Filter.OfType<T>();
        List<Preset> existing = await GetDocumentsByPredicateAsync(filter);

        if (existing.Count > 0)
        {
            return existing[0] as T;
        }

        return null;
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
