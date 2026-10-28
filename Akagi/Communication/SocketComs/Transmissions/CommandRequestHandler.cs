using Akagi.Bridge.Chat.Transmissions;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Users;

namespace Akagi.Communication.SocketComs.Transmissions;

/// <summary>
/// Sends the commands the user may run, with their parameters, so the web client can offer them as forms.
/// </summary>
internal class CommandRequestHandler : SocketTransmissionHandler
{
    public override string HandlesType => nameof(CommandRequestTransmission);

    public override Task ExecuteAsync(Context context, TransmissionWrapper transmissionWrapper)
    {
        User user = context.User ?? throw new ArgumentNullException(nameof(context), "User cannot be null in CommandRequestHandler");

        CommandResponseTransmission response = new()
        {
            Commands = [.. context.Service.AvailableCommands
                .Where(c => !c.AdminOnly || user.Admin)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => c.Describe())]
        };
        context.Session.SendTransmission(response);
        return Task.CompletedTask;
    }
}
