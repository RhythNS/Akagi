using Akagi.Characters.Groups.Checkpoints;

namespace Akagi.Communication.Commands.Groups;

internal class DeleteGroupCheckpointCommand : TextCommand
{
    public override string Name => "/deleteGroupCheckpoint";

    public override string Description => "Deletes a group checkpoint. Usage: /deleteGroupCheckpoint <checkpointId>";

    private readonly IGroupCheckpointDatabase _checkpointDatabase;

    public DeleteGroupCheckpointCommand(IGroupCheckpointDatabase checkpointDatabase)
    {
        _checkpointDatabase = checkpointDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a checkpoint ID.");
            return CommandResult.Fail("No checkpoint ID provided.");
        }

        string checkpointId = args[0];
        GroupCheckpoint? checkpoint = await _checkpointDatabase.GetDocumentByIdAsync(checkpointId);
        if (checkpoint == null)
        {
            await Communicator.SendMessage(context.User, "Checkpoint not found.");
            return CommandResult.Fail("Checkpoint not found.");
        }

        await _checkpointDatabase.DeleteDocumentByIdAsync(checkpointId);
        await Communicator.SendMessage(context.User, $"Checkpoint '{checkpointId}' deleted.");
        return CommandResult.Ok;
    }
}
