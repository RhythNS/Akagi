using Akagi.Characters.Conversations;
using Akagi.Characters.Groups.Strategies;
using Akagi.Receivers;
using Akagi.Users;

namespace Akagi.Characters.Groups;

internal interface IProactiveDelivery
{
    public Task DeliverAsync(Character character, User user, Message message);
}

/// <summary>
/// Delivers messages the character starts on its own (routines, reminders, scheduled check-ins).
/// When the character is the persona of a group, the message goes through the group so it lands in the shared
/// conversation the user actually talks in, and the persona can consult its advisors while answering.
/// </summary>
internal class ProactiveDelivery : IProactiveDelivery
{
    private readonly IReceiver _receiver;
    private readonly ICharacterGroupDatabase _groups;
    private readonly IGroupStrategyDatabase _strategies;

    public ProactiveDelivery(IReceiver receiver, ICharacterGroupDatabase groups, IGroupStrategyDatabase strategies)
    {
        _receiver = receiver;
        _groups = groups;
        _strategies = strategies;
    }

    public async Task DeliverAsync(Character character, User user, Message message)
    {
        CharacterGroup? group = await FindPersonaGroup(character, user);
        if (group != null)
        {
            await _receiver.OnGroupSystemEvent(user, group, message);
        }
        else
        {
            await _receiver.OnSystemEvent(character, user, message);
        }
    }

    private async Task<CharacterGroup?> FindPersonaGroup(Character character, User user)
    {
        List<(CharacterGroup Group, GroupStrategy? Strategy)> candidates = [];
        foreach (CharacterGroup group in await _groups.GetGroupsForUser(user))
        {
            if (!group.MemberCharacterIds.Contains(character.Id))
            {
                continue;
            }
            GroupStrategy? strategy = string.IsNullOrEmpty(group.StrategyId) ? null : await _strategies.GetDocumentByIdAsync(group.StrategyId);
            candidates.Add((group, strategy));
        }
        return SelectPersonaGroup(candidates, character.Id);
    }

    internal static CharacterGroup? SelectPersonaGroup(IEnumerable<(CharacterGroup Group, GroupStrategy? Strategy)> candidates, string? characterId)
    {
        return candidates
            .Where(c => c.Group.AllowAutomaticProcessing && c.Strategy is PersonaStrategy persona && persona.PersonaCharacterId == characterId)
            .Select(c => c.Group)
            .FirstOrDefault();
    }
}
