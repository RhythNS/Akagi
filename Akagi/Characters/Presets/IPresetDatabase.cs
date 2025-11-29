using Akagi.Data;

namespace Akagi.Characters.Presets;

internal interface IPresetDatabase : IDatabase<Preset>
{
    public Task<T?> GetPreset<T>() where T : Preset;
}
