using Akagi.Characters.Checkpoints;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class DeleteCheckpointCommand : TextCommand
{
    public override string Name => "/deleteCheckpoint";

    public override string Description => "Deletes a checkpoint by ID. Usage: /deleteCheckpoint <checkpointId>";

    private readonly ICheckpointDatabase _checkpointDatabase;

    public DeleteCheckpointCommand(ICheckpointDatabase checkpointDatabase)
    {
        _checkpointDatabase = checkpointDatabase;
    }

    public override async Task ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You need to have an active character to use this command.");
            return;
        }

        if (args.Length != 1)
        {
            await Communicator.SendMessage(context.User, "Usage: /deleteCheckpoint <checkpointId>");
            return;
        }

        string checkpointId = args[0];
        Checkpoint? checkpoint = await _checkpointDatabase.GetDocumentByIdAsync(checkpointId);
        if (checkpoint == null)
        {
            await Communicator.SendMessage(context.User, $"Checkpoint with ID '{checkpointId}' not found.");
            return;
        }

        if (checkpoint.CharacterId != context.Character.Id)
        {
            await Communicator.SendMessage(context.User, $"Checkpoint '{checkpointId}' does not belong to the active character.");
            return;
        }

        await _checkpointDatabase.DeleteDocumentByIdAsync(checkpointId);
        await Communicator.SendMessage(context.User, $"Checkpoint '{checkpointId}' deleted.");
    }
}
