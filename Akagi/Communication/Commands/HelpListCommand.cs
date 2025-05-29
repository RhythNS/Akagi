using System.Text;

namespace Akagi.Communication.Commands;

internal class HelpListCommand : ListCommand
{
    public override string Name => "/help";

    public override string Description => "Lists all available commands. Usage: /help";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        Command[] commands = [.. Communicator.AvailableCommands.OrderBy(x => x.Name)];

        if (commands == null || commands.Length == 0)
        {
            return Communicator.SendMessage(context.User, "No commands available.");
        }

        StringBuilder sb = new();
        sb.AppendLine("Available commands:\n");

        for (int i = 0; i < commands.Length; i++)
        {
            Command command = commands[i];
            sb.AppendLine($"{command.Name} - {command.Description}");
            if (i < commands.Length - 1)
            {
                sb.AppendLine("------------------------------");
            }
        }

        return Communicator.SendMessage(context.User, sb.ToString());
    }
}
