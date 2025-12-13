using Akagi.Characters.Presets.Hardcoded.Cards;
using Akagi.Characters.Presets.Hardcoded.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.Reflectors;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Characters;

[DependsOn(
    typeof(NullCardPreset),
    typeof(NullReflectorPreset),
    typeof(EthicalPuppeteerPreset)
    )]
internal class EthicalCharacterPreset : Preset
{
    private string _characterId = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        NullCardPreset card = await Load<NullCardPreset>(databaseFactory, UserId);
        NullReflectorPreset reflector = await Load<NullReflectorPreset>(databaseFactory, UserId);
        EthicalPuppeteerPreset puppeteer = await Load<EthicalPuppeteerPreset>(databaseFactory, UserId);

        Character character = new()
        {
            CardId = card.CardId,
            Name = "EthicalAi",
            PuppeteerId = puppeteer.PuppeteerId,
            ReflectorIds = [reflector.ReflectorId],
            UserId = UserId,
            TriggerPointIds = []
        };

        await Save(databaseFactory, character, CharacterId);

        CharacterId = character.Id!;
    }
}
