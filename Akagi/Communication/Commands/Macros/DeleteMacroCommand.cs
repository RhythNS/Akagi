namespace Akagi.Communication.Commands.Macros;

internal class DeleteMacroCommand : TextCommand
{
    public override string Name => "/deleteMacro";

    public override string Description => "Deletes a saved macro. Usage: /deleteMacro <name>";

    private readonly IMacroDatabase _macroDatabase;

    public DeleteMacroCommand(IMacroDatabase macroDatabase)
    {
        _macroDatabase = macroDatabase;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (args.Length < 1)
        {
            await Communicator.SendMessage(context.User, "Usage: /deleteMacro <name>");
            return CommandResult.Fail("No macro name provided.");
        }

        string macroName = args[0];
        Macro? macro = await _macroDatabase.GetMacroByNameAsync(context.User.Id!, macroName);
        if (macro == null)
        {
            await Communicator.SendMessage(context.User, $"Macro '{macroName}' not found.");
            return CommandResult.Fail($"Macro '{macroName}' not found.");
        }

        await _macroDatabase.DeleteDocumentByIdAsync(macro.Id!);
        await Communicator.SendMessage(context.User, $"Macro '{macroName}' deleted.");
        return CommandResult.Ok;
    }
}
