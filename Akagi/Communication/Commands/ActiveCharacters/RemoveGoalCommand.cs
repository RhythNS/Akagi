
using Akagi.Characters.Memories;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class RemoveGoalCommand : TextCommand
{
    public override string Name => "/removeGoal";

    public override string Description => "Removes a goal from the active character. Usage /removeGoal <index>";

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return;
        }
        if (args.Length != 0)
        {
            await Communicator.SendMessage(context.User, "You need to provide the index.");
            return;
        }
        if (int.TryParse(args[0], out int id))
        {
            await Communicator.SendMessage(context.User, "The index needs to be an integer.");
            return;
        }
        ThoughtCollection<SingleFactThought> goals = context.Character.Memory.Goals;
        if (id < 0 || id >= goals.Thoughts.Count)
        {
            await Communicator.SendMessage(context.User, "The index is out of range.");
            return;
        }
        goals.RemoveThoughtAt(id);
        await Communicator.SendMessage(context.User, "Goal removed!");
    }
}
