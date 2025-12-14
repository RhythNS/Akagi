using Akagi.Characters.Presets.Hardcoded.Cards;
using Akagi.Data;
using Akagi.Utils.Attributes;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using static Akagi.Characters.Presets.Hardcoded.Roleplayers.RoleplayConversationEndedPresets;
using static Akagi.Characters.Presets.Hardcoded.Roleplayers.RoleplayConversationSummaryPresets;
using static Akagi.Characters.Presets.Hardcoded.Roleplayers.RoleplayPuppeteerPresets;
using static Akagi.Characters.Presets.Hardcoded.Roleplayers.RoleplayReflectionPresets;

namespace Akagi.Characters.Presets.Hardcoded.Roleplayers;

[DependsOn(
    typeof(NullCardPreset),
    typeof(RoleplayPuppeteerPreset),
    typeof(RoleplayConversationEndedReflectorPreset),
    typeof(RoleplayConversationEndedTriggerPointPreset),
    typeof(RoleplayConversationSummaryReflectorPreset),
    typeof(RoleplayConversationSummaryTriggerPointPreset),
    typeof(RoleplayReflectionReflectorPreset),
    typeof(RoleplayReflectionTriggerPointPreset)
    )]
internal class RoleplayCharacterPreset : Preset
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
        RoleplayPuppeteerPreset puppeteerPreset = await Load<RoleplayPuppeteerPreset>(databaseFactory, UserId);
        RoleplayConversationEndedReflectorPreset conversationEndedReflectorPreset = await Load<RoleplayConversationEndedReflectorPreset>(databaseFactory, UserId);
        RoleplayConversationEndedTriggerPointPreset conversationEndedTriggerPreset = await Load<RoleplayConversationEndedTriggerPointPreset>(databaseFactory, UserId);
        RoleplayConversationSummaryReflectorPreset conversationSummaryReflectorPreset = await Load<RoleplayConversationSummaryReflectorPreset>(databaseFactory, UserId);
        RoleplayConversationSummaryTriggerPointPreset conversationSummaryTriggerPreset = await Load<RoleplayConversationSummaryTriggerPointPreset>(databaseFactory, UserId);
        RoleplayReflectionReflectorPreset reflectionReflectorPreset = await Load<RoleplayReflectionReflectorPreset>(databaseFactory, UserId);
        RoleplayReflectionTriggerPointPreset reflectionTriggerPreset = await Load<RoleplayReflectionTriggerPointPreset>(databaseFactory, UserId);

        Character? character = null;

        if (string.IsNullOrEmpty(CharacterId) == false)
        {
            character = await databaseFactory.GetDatabase<ICharacterDatabase>().GetCharacter(CharacterId);
        }

        character ??= new();

        character.CardId = cardPreset.CardId;
        character.Name = "Roleplay Character";
        character.PuppeteerId = puppeteerPreset.PuppeteerId;
        character.ReflectorIds =
        [
            conversationEndedReflectorPreset.ReflectorId,
            conversationSummaryReflectorPreset.ReflectorId,
            reflectionReflectorPreset.ReflectorId
        ];
        character.TriggerPointIds =
        [
            conversationEndedTriggerPreset.TriggerPointId,
            conversationSummaryTriggerPreset.TriggerPointId,
            reflectionTriggerPreset.TriggerPointId
        ];
        character.UserId = UserId;

        await Save(databaseFactory, character, CharacterId);

        CharacterId = character.Id!;
    }
}
