using Akagi.Characters.Presets.Hardcoded.Cards;
using Akagi.Characters.Presets.Hardcoded.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.Reflectors;
using Akagi.Characters.Presets.Hardcoded.TriggerPoints;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Characters;

[DependsOn(
    typeof(NullCardPreset),
    typeof(DefaultPuppeteerPreset),
    typeof(DefaultReflectorPreset),
    typeof(TriggerReflectOnCompletedConversationPreset)
    )]
internal class DefaultCharacterPreset : Preset
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
        NullCardPreset cardPreset = await Load<NullCardPreset>(databaseFactory, UserId);
        DefaultPuppeteerPreset puppeteerPreset = await Load<DefaultPuppeteerPreset>(databaseFactory, UserId);
        DefaultReflectorPreset reflectorPreset = await Load<DefaultReflectorPreset>(databaseFactory, UserId);
        TriggerReflectOnCompletedConversationPreset triggerPointPreset = await Load<TriggerReflectOnCompletedConversationPreset>(databaseFactory, UserId);

        Character character = new()
        {
            CardId = cardPreset.CardId,
            Name = "Default Character",
            PuppeteerId = puppeteerPreset.PuppeteerId,
            ReflectorIds = [reflectorPreset.ReflectorId],
            UserId = UserId,
            TriggerPointIds = [triggerPointPreset.TriggerId]
        };

        await Save(databaseFactory, character, CharacterId);

        CharacterId = character.Id!;
    }
}
