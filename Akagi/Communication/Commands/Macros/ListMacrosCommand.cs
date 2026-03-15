using System.Text;

namespace Akagi.Communication.Commands.Macros;

internal class ListMacrosCommand : TextCommand
{
    public override string Name => "/listMacros";

    public override string Description => "Lists all your saved macros. Usage: /listMacros";

    private readonly IMacroDatabase _macroDatabase;

    public ListMacrosCommand(IMacroDatabase macroDatabase)
    {
        _macroDatabase = macroDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        List<Macro> macros = await _macroDatabase.GetMacrosForUserAsync(context.User.Id!);
        if (macros.Count == 0)
        {
            await Communicator.SendMessage(context.User, "You have no saved macros.");
            return CommandResult.Ok;
        }

        StringBuilder sb = new();
        sb.AppendLine("Your macros:");
        foreach (Macro macro in macros)
        {
            sb.Append($"  {macro.Name} ({macro.Steps.Count} step(s))");
            if (macro.DynamicVariableNames.Count > 0)
            {
                sb.Append($" [dynamic: {string.Join(", ", macro.DynamicVariableNames.Select(n => $"${n}"))}]");
            }
            if (macro.StaticVariables.Count > 0)
            {
                sb.Append($" [static: {string.Join(", ", macro.StaticVariables.Select(kv => $"{kv.Key}={kv.Value}"))}]");
            }
            sb.AppendLine();
        }

        await Communicator.SendMessage(context.User, sb.ToString().TrimEnd());
        return CommandResult.Ok;
    }
}
