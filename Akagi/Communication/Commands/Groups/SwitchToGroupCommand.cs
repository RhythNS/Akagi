using Akagi.Characters.Groups;
using Akagi.Communication.TelegramComs;

namespace Akagi.Communication.Commands.Groups;

internal class SwitchToGroupCommand : TextCommand
{
    public override string Name => "/switchToGroup";

    public override string Description => "Switches to a group as the active target. Usage: /switchToGroup <groupName>";

    public override Type[] CompatibleFor => [typeof(TelegramService)];

    private readonly ICharacterGroupDatabase _groupDatabase;

    public SwitchToGroupCommand(ICharacterGroupDatabase groupDatabase)
    {
        _groupDatabase = groupDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Please provide a group name.");
            return CommandResult.Fail("No group name provided.");
        }

        string name = string.Join(' ', args);
        List<CharacterGroup> groups = await _groupDatabase.GetGroupsForUser(context.User);
        CharacterGroup? group = groups.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (group == null)
        {
            await Communicator.SendMessage(context.User, "Group not found.");
            return CommandResult.Fail("Group not found.");
        }

        context.User.TelegramUser!.CurrentGroupId = group.Id!;
        await Communicator.SendMessage(context.User, $"Switched to group '{group.Name}'.");
        return CommandResult.Ok;
    }
}
