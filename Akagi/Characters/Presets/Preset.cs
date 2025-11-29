using Akagi.Data;

namespace Akagi.Characters.Presets;

internal abstract class Preset : Savable
{
    public abstract Task CreateAsync(IDatabaseFactory databaseFactory);

    protected static async Task<T> Load<T>(IDatabaseFactory databaseFactory) where T : Preset
    {
        return await databaseFactory.GetDatabase<IPresetDatabase>().GetPreset<T>()
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
