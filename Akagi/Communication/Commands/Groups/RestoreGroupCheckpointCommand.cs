using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Checkpoints;

namespace Akagi.Communication.Commands.Groups;

internal class RestoreGroupCheckpointCommand : TextCommand
{
    public override string Name => "/restoreGroupCheckpoint";

    public override string Description => "Restores the active group from a checkpoint. Usage: /restoreGroupCheckpoint <checkpointId>";

    private readonly ICharacterGroupDatabase _groupDatabase;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly IGroupCheckpointDatabase _checkpointDatabase;

    public RestoreGroupCheckpointCommand(ICharacterGroupDatabase groupDatabase, ICharacterDatabase characterDatabase, IGroupCheckpointDatabase checkpointDatabase)
    {
        _groupDatabase = groupDatabase;
        _characterDatabase = characterDatabase;
        _checkpointDatabase = checkpointDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Group == null)
        {
            await Communicator.SendMessage(context.User, "No active group. Use /switchToGroup first.");
            return CommandResult.Fail("No active group.");
        }

        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a checkpoint ID.");
            return CommandResult.Fail("No checkpoint ID provided.");
        }

        string checkpointId = args[0];
        GroupCheckpoint? checkpoint = await _checkpointDatabase.GetDocumentByIdAsync(checkpointId);
        if (checkpoint == null || checkpoint.GroupId != context.Group.Id!)
        {
            await Communicator.SendMessage(context.User, "Checkpoint not found or does not belong to the active group.");
            return CommandResult.Fail("Checkpoint not found.");
        }

        List<Character> members = [];
        foreach (string memberId in context.Group.MemberCharacterIds)
        {
            Character? member = await _characterDatabase.GetCharacter(memberId);
            if (member != null)
            {
                members.Add(member);
            }
        }

        checkpoint.ApplyToGroup(context.Group, members);
        await Communicator.SendMessage(context.User, $"Group restored from checkpoint '{checkpointId}'.");
        return CommandResult.Ok;
    }
}
