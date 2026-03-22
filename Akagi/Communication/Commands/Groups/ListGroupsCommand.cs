using Akagi.Characters.Groups;

namespace Akagi.Communication.Commands.Groups;

internal class ListGroupsCommand : Lists.ListCommand
{
    public override string Name => "/listGroups";

    public override string Description => "Lists all groups for the current user. Usage: /listGroups";

    private readonly ICharacterGroupDatabase _groupDatabase;

    public ListGroupsCommand(ICharacterGroupDatabase groupDatabase)
    {
        _groupDatabase = groupDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] _)
    {
        List<CharacterGroup> groups = await _groupDatabase.GetGroupsForUser(context.User);
        if (groups.Count == 0)
        {
            await Communicator.SendMessage(context.User, "No groups found.");
            return CommandResult.Ok;
        }
        string[] ids = [.. groups.Select(x => x.Id!)];
        string[] names = [.. groups.Select(x => x.Name)];
        string choices = GetIdList(ids, names);
        await Communicator.SendMessage(context.User, $"Available groups:\n{choices}");
        return CommandResult.Ok;
    }
}
