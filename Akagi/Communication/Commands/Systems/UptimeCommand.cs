using Akagi.Utils;
using Akagi.Utils.Extensions;

namespace Akagi.Communication.Commands.Systems;

internal class UptimeCommand : TextCommand
{
    public override string Name => "/uptime";

    public override string Description => "Retrieves the uptime of the application.";

    private readonly ApplicationInformation _applicationInformation;

    public UptimeCommand(ApplicationInformation applicationInformation)
    {
        _applicationInformation = applicationInformation;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        TimeSpan uptime = _applicationInformation.Uptime;

        string message = $"Application has been running for {uptime.StringifyPrecise()}.";

        return Communicator.SendMessage(context.User, message);
    }
}
