using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.SystemProcessors;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Puppeteers;

[DependsOn(typeof(EthicalProcessorPreset))]
internal class EthicalPuppeteerPreset : Preset
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
        EthicalProcessorPreset ethicalProcessor = await Load<EthicalProcessorPreset>(databaseFactory, UserId);
        
        SinglePuppeteer singlePuppeteer = new()
        {
            Name = "Ethical Puppeteer",
            SystemProcessorId = ethicalProcessor.ProcessorId!,
        };

        await Save(databaseFactory, singlePuppeteer, PuppeteerId);
        PuppeteerId = singlePuppeteer.Id!;
    }
}
