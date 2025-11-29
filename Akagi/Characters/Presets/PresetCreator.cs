using Akagi.Data;
using Akagi.Flow;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.Presets;

internal class PresetCreator : ISystemInitializer
{
    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILogger<PresetCreator> _logger;

    public PresetCreator(IDatabaseFactory databaseFactory, ILogger<PresetCreator> logger)
    {
        _databaseFactory = databaseFactory;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Creating default presets...");

        IPresetDatabase presetDatabase = _databaseFactory.GetDatabase<IPresetDatabase>();

        List<Type> presetTypes = [.. AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsSubclassOf(typeof(Preset)) && !type.IsAbstract)
            .SortByDependencies()];

        List<Preset> presets = await presetDatabase.GetDocumentsAsync();

        foreach (Type presetType in presetTypes)
        {
            try
            {
                Preset preset = presets.FirstOrDefault(p => p.GetType() == presetType)
                    ?? (Preset)Activator.CreateInstance(presetType)!;

                await preset.CreateAsync(_databaseFactory);

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
