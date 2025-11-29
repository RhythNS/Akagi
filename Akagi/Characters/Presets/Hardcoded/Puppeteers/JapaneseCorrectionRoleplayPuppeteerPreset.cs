using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.SystemProcessors;
using Akagi.Data;
using Akagi.Utils.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Puppeteers;

[DependsOn(typeof(RoleplayProcessorPreset), typeof(JapaneseCorrectionProcessorPreset))]
internal class JapaneseCorrectionRoleplayPuppeteerPreset : Preset
{
    private string _puppeteerId = string.Empty;

    public string PuppeteerId
    {
        get => _puppeteerId;
        set => SetProperty(ref _puppeteerId, value);
    }

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        RoleplayProcessorPreset roleplay = await Load<RoleplayProcessorPreset>(databaseFactory);
        JapaneseCorrectionProcessorPreset japaneseCorrection = await Load<JapaneseCorrectionProcessorPreset>(databaseFactory);

        LinePuppeteer puppeteer = new()
        {
            Id = PuppeteerId,
            Name = "Japanese Correction Roleplay Puppeteer",
            Definitions =
            [
                new LinePuppeteer.Definition
                {
                    SystemProcessorId = japaneseCorrection.ProcessorId,
                },
                new LinePuppeteer.Definition
                {
                    SystemProcessorId = roleplay.ProcessorId,
                },
            ],
        };

        await Save(databaseFactory, puppeteer, PuppeteerId);

        PuppeteerId = puppeteer.Id;
    }
}
