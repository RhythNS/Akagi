using Akagi.Data;
using Akagi.Users;

namespace Akagi.Characters.TriggerPoints;

/// <summary>
/// Fires a character's trigger points for an event. Used for single-character turns and for the persona of a group.
/// </summary>
internal static class TriggerPointRunner
{
    public static async Task FireAsync(TriggerPoint.TriggerType type, Character character, User user, IDatabaseFactory databaseFactory)
    {
        if (character.AllowAutomaticProcessing == false)
        {
            return;
        }
        foreach (string triggerId in character.TriggerPointIds)
        {
            TriggerPoint triggerPoint = await databaseFactory.GetDatabase<ITriggerPointDatabase>().GetDocumentByIdAsync(triggerId)
                ?? throw new Exception($"TriggerPoint with ID {triggerId} not found for character {character.Id}");
            await using TriggerPoint.TriggerContext context = new()
            {
                Character = character,
                User = user,
                DatabaseFactory = databaseFactory,
            };
            await triggerPoint.Init(context);
            await triggerPoint.On(type);
            await databaseFactory.SaveIfDirty(triggerPoint);
        }
    }
}
