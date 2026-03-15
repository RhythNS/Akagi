
using Akagi.Characters.Memories;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class AddGoalCommand : TextCommand
{
    public override string Name => "/addGoal";

    public override string Description => "Adds a goal to the active character. Usage /addGoal <goal>";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length == 0)
        {
            await Communicator.SendMessage(context.User, "You need to provide a goal.");
            return CommandResult.Fail("No goal provided.");
        }
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }
        string goal = string.Join(" ", args);
        SingleFactThought singleFact = new()
        {
            Fact = goal,
            Timestamp = DateTime.UtcNow
        };
        context.Character.Memory.Goals.AddThought(singleFact);

        await Communicator.SendMessage(context.User, $"Added goal: {goal}");
        return CommandResult.Ok;
    }
}
