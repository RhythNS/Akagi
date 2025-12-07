using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.SystemProcessors;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Puppeteers;

[DependsOn(typeof(DefaultProcessorPreset))]
internal class DefaultPuppeteerPreset : Preset
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
        DefaultProcessorPreset roleplayProcessor = await Load<DefaultProcessorPreset>(databaseFactory, UserId);

        SinglePuppeteer singlePuppeteer = new()
        {
            Name = "Roleplay Puppeteer",
            SystemProcessorId = roleplayProcessor.ProcessorId!,
        };

        await Save(databaseFactory, singlePuppeteer, PuppeteerId);

        PuppeteerId = singlePuppeteer.Id!;
    }
}
