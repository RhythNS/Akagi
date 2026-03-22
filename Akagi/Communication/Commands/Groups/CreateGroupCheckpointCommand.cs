using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Characters.Groups.Checkpoints;

namespace Akagi.Communication.Commands.Groups;

internal class CreateGroupCheckpointCommand : TextCommand
{
    public override string Name => "/createGroupCheckpoint";

    public override string Description => "Creates a checkpoint for the active group. Usage: /createGroupCheckpoint";

    private readonly ICharacterGroupDatabase _groupDatabase;
    private readonly ICharacterDatabase _characterDatabase;
    private readonly IGroupCheckpointDatabase _checkpointDatabase;

    public CreateGroupCheckpointCommand(ICharacterGroupDatabase groupDatabase, ICharacterDatabase characterDatabase, IGroupCheckpointDatabase checkpointDatabase)
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

        List<Character> members = [];
        foreach (string memberId in context.Group.MemberCharacterIds)
        {
            Character? member = await _characterDatabase.GetCharacter(memberId);
            if (member != null)
            {
                members.Add(member);
            }
        }

        GroupCheckpoint checkpoint = GroupCheckpoint.CreateFromGroup(context.Group, members);
        await _checkpointDatabase.SaveDocumentAsync(checkpoint);
        await Communicator.SendMessage(context.User, $"Group checkpoint '{checkpoint.Id}' created.");
        return CommandResult.Ok;
    }
}
