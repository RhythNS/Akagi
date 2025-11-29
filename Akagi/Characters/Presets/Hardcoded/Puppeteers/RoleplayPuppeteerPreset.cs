using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.SystemProcessors;
using Akagi.Data;
using Akagi.Utils.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Puppeteers;

[DependsOn(typeof(RoleplayProcessorPreset))]
internal class RoleplayPuppeteerPreset : Preset
{
    private string _puppeteerId = string.Empty;

    public string PuppeteerId
    {
        get => _puppeteerId;
        set => SetProperty(ref _puppeteerId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        RoleplayProcessorPreset roleplayProcessor = await Load<RoleplayProcessorPreset>(databaseFactory);

        SinglePuppeteer singlePuppeteer = new()
        {
            Name = "Roleplay Puppeteer",
            SystemProcessorId = roleplayProcessor.Id!,
        };

        await Save(databaseFactory, singlePuppeteer, PuppeteerId);

        PuppeteerId = singlePuppeteer.Id!;
    }
}
