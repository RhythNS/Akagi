namespace Akagi.Communication.Commands.Macros;

internal class RunMacroCommand : TextCommand
{
    public override string Name => "/runMacro";

    public override string Description => "Runs a saved macro. Usage: /runMacro <name> [dynamic variable values in order]";

    private readonly IMacroDatabase _macroDatabase;

    public RunMacroCommand(IMacroDatabase macroDatabase)
    {
        _macroDatabase = macroDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Usage: /runMacro <name> [dynamic variable values...]");
            return CommandResult.Fail("No macro name provided.");
        }

        string macroName = args[0];
        Macro? macro = await _macroDatabase.GetMacroByNameAsync(context.User.Id!, macroName);
        if (macro == null)
        {
            await Communicator.SendMessage(context.User, $"Macro '{macroName}' not found.");
            return CommandResult.Fail($"Macro '{macroName}' not found.");
        }

        Dictionary<string, string> variables = new(macro.StaticVariables);

        string[] dynamicValues = args.Skip(1).ToArray();
        if (dynamicValues.Length != macro.DynamicVariableNames.Count)
        {
            string expected = string.Join(", ", macro.DynamicVariableNames.Select(n => $"${n}"));
            await Communicator.SendMessage(context.User,
                $"Macro '{macroName}' expects {macro.DynamicVariableNames.Count} dynamic variable(s): {expected}. Got {dynamicValues.Length}.");
            return CommandResult.Fail("Dynamic variable count mismatch.");
        }

        for (int i = 0; i < macro.DynamicVariableNames.Count; i++)
        {
            variables[macro.DynamicVariableNames[i]] = dynamicValues[i];
        }

        Command[] availableCommands = Communicator.AvailableCommands;
        for (int stepIndex = 0; stepIndex < macro.Steps.Count; stepIndex++)
        {
            MacroStep step = macro.Steps[stepIndex];
            Command? command = availableCommands.FirstOrDefault(c =>
                string.Equals(c.Name, step.CommandName, StringComparison.OrdinalIgnoreCase));

            if (command == null)
            {
                await Communicator.SendMessage(context.User,
                    $"Macro '{macroName}' failed at step {stepIndex + 1}: unknown command '{step.CommandName}'.");
                return CommandResult.Fail($"Unknown command '{step.CommandName}' at step {stepIndex + 1}.");
            }

            if (command is not TextCommand textCommand)
            {
                await Communicator.SendMessage(context.User,
                    $"Macro '{macroName}' failed at step {stepIndex + 1}: '{step.CommandName}' is not a text command.");
                return CommandResult.Fail($"Command '{step.CommandName}' is not a text command.");
            }

            string[] stepArgs = ParseArguments(step.Arguments);
            stepArgs = ResolveVariables(stepArgs, variables);

            CommandResult result = await textCommand.ExecuteAsync(context, stepArgs);
            if (!result.Success)
            {
                await Communicator.SendMessage(context.User,
                    $"Macro '{macroName}' stopped at step {stepIndex + 1} ({step.CommandName}): {result.Error}");
                return CommandResult.Fail($"Macro stopped at step {stepIndex + 1}: {result.Error}");
            }
        }

        await Communicator.SendMessage(context.User, $"Macro '{macroName}' completed successfully ({macro.Steps.Count} step(s)).");
        return CommandResult.Ok;
    }
}
