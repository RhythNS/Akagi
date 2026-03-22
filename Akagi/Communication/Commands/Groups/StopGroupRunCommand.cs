using Akagi.Characters.Groups;
using Akagi.Receivers;

namespace Akagi.Communication.Commands.Groups;

internal class StopGroupRunCommand : TextCommand
{
    public override string Name => "/stopGroupRun";

    public override string Description => "Cancels a running group execution. Usage: /stopGroupRun";

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        if (context.Group == null)
        {
            await Communicator.SendMessage(context.User, "No active group.");
            return CommandResult.Fail("No active group.");
        }

        bool cancelled = Receiver.TryCancelGroupRun(context.User.Id!, context.Group.Id!);
        if (cancelled)
        {
            await Communicator.SendMessage(context.User, "Group run cancellation requested.");
        }
        else
        {
            await Communicator.SendMessage(context.User, "No active group run to cancel.");
        }
        return CommandResult.Ok;
    }
}
