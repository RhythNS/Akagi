using Akagi.Characters.Checkpoints;

namespace Akagi.Communication.Commands.Lists;

internal class ListCheckpointsCommand : ListCommand
{
    public override string Name => "/listCheckpoints";

    public override string Description => "Lists all checkpoints for the active character. Usage: /listCheckpoints";

    private readonly ICheckpointDatabase _checkpointDatabase;

    public ListCheckpointsCommand(ICheckpointDatabase checkpointDatabase)
    {
        _checkpointDatabase = checkpointDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return CommandResult.Fail("No active character.");
        }

        Checkpoint[] checkpoints = await _checkpointDatabase.GetCheckpointsForCharacterAsync(context.Character.Id!);
        if (checkpoints.Length == 0)
        {
            await Communicator.SendMessage(context.User, "No checkpoints found for this character.");
            return CommandResult.Ok;
        }

        string[] ids = [.. checkpoints.Select(c => c.Id!)];
        string[] names = [.. checkpoints.Select(c => c.CreatedOn.ToString("yyyy-MM-dd HH:mm:ss"))];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Checkpoints for {context.Character.Name}:\n{choices}");
        return CommandResult.Ok;
    }
}
