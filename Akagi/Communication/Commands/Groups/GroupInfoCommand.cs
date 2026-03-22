using Akagi.Characters;
using Akagi.Characters.Groups;

namespace Akagi.Communication.Commands.Groups;

internal class GroupInfoCommand : TextCommand
{
    public override string Name => "/groupInfo";

    public override string Description => "Shows details about the active group. Usage: /groupInfo";

    private readonly ICharacterGroupDatabase _groupDatabase;
    private readonly ICharacterDatabase _characterDatabase;

    public GroupInfoCommand(ICharacterGroupDatabase groupDatabase, ICharacterDatabase characterDatabase)
    {
        _groupDatabase = groupDatabase;
        _characterDatabase = characterDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Group == null)
        {
            await Communicator.SendMessage(context.User, "No active group. Use /switchToGroup first.");
            return CommandResult.Fail("No active group.");
        }

        List<string> memberNames = [];
        foreach (string memberId in context.Group.MemberCharacterIds)
        {
            Character? member = await _characterDatabase.GetCharacter(memberId);
            memberNames.Add(member != null ? $"{member.Name} ({member.Id})" : $"Unknown ({memberId})");
        }

        string info = $"""
            Group: {context.Group.Name}
            Members: {string.Join(", ", memberNames)}
            Visibility: {context.Group.UserVisibility}
            Error Behavior: {context.Group.ErrorBehavior}
            Auto Processing: {context.Group.AllowAutomaticProcessing}
            Conversations: {context.Group.Conversations.List.Count}
            """;

        await Communicator.SendMessage(context.User, info);
        return CommandResult.Ok;
    }
}
