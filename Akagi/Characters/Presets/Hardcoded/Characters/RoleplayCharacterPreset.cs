using Akagi.Characters.Presets.Hardcoded.Cards;
using Akagi.Characters.Presets.Hardcoded.Puppeteers;
using Akagi.Characters.Presets.Hardcoded.Reflectors;
using Akagi.Characters.Presets.Hardcoded.TriggerPoints;
using Akagi.Characters.Presets.Hardcoded.Users;
using Akagi.Data;
using Akagi.Utils.Attributes;

namespace Akagi.Characters.Presets.Hardcoded.Characters;

[DependsOn(
    typeof(NullUserPreset),
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

    public override async Task CreateAsync(IDatabaseFactory databaseFactory)
    {
        NullUserPreset userPreset = await Load<NullUserPreset>(databaseFactory);
        NullCardPreset cardPreset = await Load<NullCardPreset>(databaseFactory);
        RoleplayPuppeteerPreset puppeteerPreset = await Load<RoleplayPuppeteerPreset>(databaseFactory);
        DefaultReflectorPreset reflectorPreset = await Load<DefaultReflectorPreset>(databaseFactory);
        TriggerReflectOnCompletedConversationPreset triggerPointPreset = await Load<TriggerReflectOnCompletedConversationPreset>(databaseFactory);

        Character character = new()
        {
            CardId = cardPreset.CardId,
            Name = "Roleplay Character",
            PuppeteerId = puppeteerPreset.PuppeteerId,
            ReflectorIds = [reflectorPreset.ReflectorId],
            UserId = userPreset.UserId,
            TriggerPointIds = [triggerPointPreset.TriggerId]
        };

        await Save(databaseFactory, character, CharacterId);

        CharacterId = character.Id!;
    }
}
