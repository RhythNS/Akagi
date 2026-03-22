using Akagi.Characters.Groups.Checkpoints;

namespace Akagi.Communication.Commands.Groups;

internal class ListGroupCheckpointsCommand : Lists.ListCommand
{
    public override string Name => "/listGroupCheckpoints";

    public override string Description => "Lists checkpoints for the active group. Usage: /listGroupCheckpoints";

    private readonly IGroupCheckpointDatabase _checkpointDatabase;

    public ListGroupCheckpointsCommand(IGroupCheckpointDatabase checkpointDatabase)
    {
        _checkpointDatabase = checkpointDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Group == null)
        {
            await Communicator.SendMessage(context.User, "No active group. Use /switchToGroup first.");
            return CommandResult.Fail("No active group.");
        }

        GroupCheckpoint[] checkpoints = await _checkpointDatabase.GetCheckpointsForGroupAsync(context.Group.Id!);
        if (checkpoints.Length == 0)
        {
            await Communicator.SendMessage(context.User, "No checkpoints found for this group.");
            return CommandResult.Ok;
        }

        string[] ids = [.. checkpoints.Select(c => c.Id!)];
        string[] names = [.. checkpoints.Select(c => $"{c.GroupName} ({c.CreatedOn:g})")];
        string list = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Group checkpoints:\n{list}");
        return CommandResult.Ok;
    }
}
