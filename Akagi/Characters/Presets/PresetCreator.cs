using Akagi.Data;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.Presets;

internal interface IPresetCreator
{
    public Task CreateForUser(string userId);
}

internal class PresetCreator : IPresetCreator
{
    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILogger<PresetCreator> _logger;

    public PresetCreator(IDatabaseFactory databaseFactory, ILogger<PresetCreator> logger)
    {
        _databaseFactory = databaseFactory;
        _logger = logger;
    }

    public async Task CreateForUser(string userId)
    {
        _logger.LogInformation("Creating default presets for {userId}...", userId);

        IPresetDatabase presetDatabase = _databaseFactory.GetDatabase<IPresetDatabase>();

        List<Type> presetTypes = [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(Preset)) && !type.IsAbstract)
            .SortByDependencies()];

        List<Preset> presets = await presetDatabase.GetAllPresets(userId);

        foreach (Type presetType in presetTypes)
        {
            try
            {
                Preset preset = presets.FirstOrDefault(p => p.GetType() == presetType)
                    ?? (Preset)Activator.CreateInstance(presetType)!;

                await preset.CreateAsync(_databaseFactory, userId);

                await presetDatabase.SaveAsync(preset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create default preset of type {PresetType}", presetType.Name);
                continue;
            }

            _logger.LogInformation("Created default preset: {PresetType}", presetType.Name);
        }

        _logger.LogInformation("Default preset creation complete.");
    }
}
