using Akagi.Characters.Presets.Hardcoded.Cards;
using Akagi.Characters.Presets.Hardcoded.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.Reflectors;
using Akagi.Characters.Presets.Hardcoded.TriggerPoints;
using Akagi.Data;
using Akagi.Utils.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Characters;

[DependsOn(
    typeof(NullCardPreset),
    typeof(RoleplayPuppeteerPreset),
    typeof(DefaultReflectorPreset),
    typeof(TriggerReflectOnCompletedConversationPreset)
    )]
internal class RoleplayCharacterPreset : Preset
{
    private string _characterId = string.Empty;

    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }

    protected override async Task CreateInnerAsync(IDatabaseFactory databaseFactory)
    {
        NullCardPreset cardPreset = await Load<NullCardPreset>(databaseFactory, UserId);
        RoleplayPuppeteerPreset puppeteerPreset = await Load<RoleplayPuppeteerPreset>(databaseFactory, UserId);
        DefaultReflectorPreset reflectorPreset = await Load<DefaultReflectorPreset>(databaseFactory, UserId);
        TriggerReflectOnCompletedConversationPreset triggerPointPreset = await Load<TriggerReflectOnCompletedConversationPreset>(databaseFactory, UserId);

        Character character = new()
        {
            CardId = cardPreset.CardId,
            Name = "Roleplay Character",
            PuppeteerId = puppeteerPreset.PuppeteerId,
            ReflectorIds = [reflectorPreset.ReflectorId],
            UserId = UserId,
            TriggerPointIds = [triggerPointPreset.TriggerId]
        };

        await Save(databaseFactory, character, CharacterId);

        CharacterId = character.Id!;
    }
}
