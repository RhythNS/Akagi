using Akagi.Characters.Groups.Strategies;
using Akagi.Data;

namespace Akagi.Characters.Groups;

/// <summary>
/// The member that represents a group to the user: the persona of a <see cref="PersonaStrategy"/>, otherwise the first member.
/// Its card is the group's picture.
/// </summary>
internal static class GroupSpeaker
{
    public static async Task<string?> GetIdAsync(CharacterGroup group, IDatabaseFactory databaseFactory)
    {
        GroupStrategy? strategy = string.IsNullOrEmpty(group.StrategyId)
            ? null
            : await databaseFactory.GetDatabase<IGroupStrategyDatabase>().GetDocumentByIdAsync(group.StrategyId);
        return Select(group, strategy);
    }

    public static string? Select(CharacterGroup group, GroupStrategy? strategy)
    {
        return strategy is PersonaStrategy persona && group.MemberCharacterIds.Contains(persona.PersonaCharacterId)
            ? persona.PersonaCharacterId
            : group.MemberCharacterIds.FirstOrDefault();
    }
}
