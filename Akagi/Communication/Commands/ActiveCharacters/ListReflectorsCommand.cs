
using Akagi.Characters.CharacterBehaviors.Reflectors;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class ListReflectorsCommand : TextCommand
{
    public override string Name => "/listReflectors";

    public override string Description => "Lists all reflectors associated with the active character.";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You must select a character to use this command.");
            return;
        }
        string[] reflectorIds = context.Character.ReflectorIds;
        if (reflectorIds.Length == 0)
        {
            await Communicator.SendMessage(context.User, "The active character has no reflectors.");
            return;
        }

        IReflectorDatabase database = context.DatabaseFactory.GetDatabase<IReflectorDatabase>();
        List<Reflector> reflectors = new(reflectorIds.Length);
        foreach (string reflectorId in reflectorIds)
        {
            Reflector? reflector = await database.GetDocumentByIdAsync(reflectorId);
            if (reflector != null)
            {
                reflectors.Add(reflector);
            }
        }

        string reflectorList = $"Reflectors for character '{context.Character.Name}':\n" +
            string.Join("\n", reflectors.Select(r => $"- {r.Name} (ID: {r.Id})"));

        await Communicator.SendMessage(context.User, reflectorList);
    }
}
