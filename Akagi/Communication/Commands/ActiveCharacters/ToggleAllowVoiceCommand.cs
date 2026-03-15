namespace Akagi.Communication.Commands.ActiveCharacters;

internal class ToggleAllowVoiceCommand : TextCommand
{
    public override string Name => "/toggleAllowVoice";

    public override string Description => "Toggle whether the character is allowed to use voice. Usage: /toggleAllowVoice";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Character == null)
        {
            await Communicator.SendMessage(context.User, "You must have an active character to toggle voice.");
            return CommandResult.Fail("No active character.");
        }

        context.Character.AllowVoice = !context.Character.AllowVoice;

        await Communicator.SendMessage(context.User, $"Character voice is now {(context.Character.AllowVoice ? "enabled" : "disabled")}.");
        return CommandResult.Ok;
    }
}
