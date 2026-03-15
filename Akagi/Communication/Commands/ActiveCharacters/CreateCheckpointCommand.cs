using Akagi.Characters.Checkpoints;

namespace Akagi.Communication.Commands.ActiveCharacters;

internal class CreateCheckpointCommand : TextCommand
{
    public override string Name => "/createCheckpoint";

    public override string Description => "Creates a checkpoint for the active character. Usage: /createCheckpoint";

    private readonly ICheckpointDatabase _checkpointDatabase;

    public CreateCheckpointCommand(ICheckpointDatabase checkpointDatabase)
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

        Checkpoint checkpoint = Checkpoint.CreateFromCharacter(context.Character);
        await _checkpointDatabase.SaveDocumentAsync(checkpoint);
        await Communicator.SendMessage(context.User, $"Checkpoint '{checkpoint.Id}' created.");
    }
}
