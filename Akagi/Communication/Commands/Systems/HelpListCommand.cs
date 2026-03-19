using Akagi.Communication.Commands.Lists;
using System.Text;

namespace Akagi.Communication.Commands.Systems;

internal class HelpListCommand : ListCommand
{
    public override string Name => "/help";

    public override string Description => "Lists all available commands. Usage: /help <searchTerm>";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        string? searchTerm = args.Length < 1 ? null : args[0].ToLowerInvariant();

        Command[] commands =
        [
            .. Communicator.AvailableCommands
            .Where(x =>
            {
                if (searchTerm != null)
                {
                    return x.Name.Contains(searchTerm,
                        StringComparison.InvariantCultureIgnoreCase);
                }

               return !x.AdminOnly || context.User.Admin;
            }
            ).OrderBy(x => x.Name)
        ];

        if (commands == null || commands.Length == 0)
        {
            await Communicator.SendMessage(context.User, "No commands available.");
            return CommandResult.Ok;
        }

        StringBuilder sb = new();
        sb.AppendLine("Available commands:\n");

        for (int i = 0; i < commands.Length; i++)
        {
            Command command = commands[i];
            sb.AppendLine($"{command.Name} - {command.Description}");
        }

        await Communicator.SendMessage(context.User, sb.ToString());
        return CommandResult.Ok;
    }
}
