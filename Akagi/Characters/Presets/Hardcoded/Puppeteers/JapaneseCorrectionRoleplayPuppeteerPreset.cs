using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.SystemProcessors;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Puppeteers;

[DependsOn(typeof(DefaultProcessorPreset), typeof(JapaneseCorrectionProcessorPreset))]
internal class JapaneseCorrectionRoleplayPuppeteerPreset : Preset
{
    private string _puppeteerId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string PuppeteerId
    {
        get => _puppeteerId;
        set => SetProperty(ref _puppeteerId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        DefaultProcessorPreset roleplay = await Load<DefaultProcessorPreset>(databaseFactory, UserId);
        JapaneseCorrectionProcessorPreset japaneseCorrection = await Load<JapaneseCorrectionProcessorPreset>(databaseFactory, UserId);

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
