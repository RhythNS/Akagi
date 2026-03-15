namespace Akagi.Communication.Commands.ActiveCharacters;

internal class ToggleAllowAutomaticProcessingCommand : TextCommand
{
    public override string Name => "/toggleAllowAutomaticProcessing";

    public override string Description => "Toggle whether the character is allowed to use automatic processing. Usage: /toggleAllowAutomaticProcessing";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You must have an active character to toggle automatic processing.");
            return CommandResult.Fail("No active character.");
        }

        context.Character.AllowAutomaticProcessing = !context.Character.AllowAutomaticProcessing;

        await Communicator.SendMessage(context.User, $"Character automatic processing is now {(context.Character.AllowAutomaticProcessing ? "enabled" : "disabled")}.");
        return CommandResult.Ok;
    }
}
