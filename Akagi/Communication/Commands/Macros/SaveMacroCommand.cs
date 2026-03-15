namespace Akagi.Communication.Commands.Macros;

internal class SaveMacroCommand : TextCommand
{
    public override string Name => "/saveMacro";

    public override string Description => "Saves a macro. Usage: /saveMacro <name> <steps> where steps are semicolon-separated commands. " +
        "Use $varName for dynamic variables. Use static:key=value to define static variables. " +
        "Example: /saveMacro myMacro static:name=hello /changeName $name;/ping";

    private readonly IMacroDatabase _macroDatabase;

    public SaveMacroCommand(IMacroDatabase macroDatabase)
    {
        _macroDatabase = macroDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 2)
        {
            await Communicator.SendMessage(context.User, "Usage: /saveMacro <name> [static:key=value ...] <steps (semicolon-separated)>");
            return CommandResult.Fail("Invalid arguments.");
        }

        string macroName = args[0];
        Dictionary<string, string> staticVariables = [];
        List<string> dynamicVariableNames = [];
        string? stepsString = null;

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("static:", StringComparison.OrdinalIgnoreCase))
            {
                string kvp = args[i]["static:".Length..];
                int eqIndex = kvp.IndexOf('=');
                if (eqIndex > 0)
                {
                    staticVariables[kvp[..eqIndex]] = kvp[(eqIndex + 1)..];
                }
            }
            else
            {
                stepsString = string.Join(' ', args.Skip(i));
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(stepsString))
        {
            await Communicator.SendMessage(context.User, "No command steps provided.");
            return CommandResult.Fail("No steps provided.");
        }

        string[] stepParts = stepsString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<MacroStep> steps = [];

        foreach (string stepPart in stepParts)
        {
            int spaceIndex = stepPart.IndexOf(' ');
            string commandName;
            string arguments;
            if (spaceIndex == -1)
            {
                commandName = stepPart;
                arguments = string.Empty;
            }
            else
            {
                commandName = stepPart[..spaceIndex];
                arguments = stepPart[(spaceIndex + 1)..];
            }

            steps.Add(new MacroStep
            {
                CommandName = commandName,
                Arguments = arguments
            });

            foreach (string arg in ParseArguments(arguments))
            {
                if (arg.StartsWith('$') && arg.Length > 1)
                {
                    string varName = arg[1..];
                    if (!staticVariables.ContainsKey(varName) && !dynamicVariableNames.Contains(varName))
                    {
                        dynamicVariableNames.Add(varName);
                    }
                }
            }
        }

        Macro? existing = await _macroDatabase.GetMacroByNameAsync(context.User.Id!, macroName);
        if (existing != null)
        {
            existing.Steps = steps;
            existing.StaticVariables = staticVariables;
            existing.DynamicVariableNames = dynamicVariableNames;
            await _macroDatabase.SaveDocumentAsync(existing);
            await Communicator.SendMessage(context.User, $"Macro '{macroName}' updated with {steps.Count} step(s).");
        }
        else
        {
            Macro macro = new()
            {
                Name = macroName,
                UserId = context.User.Id!,
                Steps = steps,
                StaticVariables = staticVariables,
                DynamicVariableNames = dynamicVariableNames
            };
            await _macroDatabase.SaveDocumentAsync(macro);
            await Communicator.SendMessage(context.User, $"Macro '{macroName}' saved with {steps.Count} step(s).");
        }

        return CommandResult.Ok;
    }
}
