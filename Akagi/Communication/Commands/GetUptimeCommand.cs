using Akagi.Utils;
using Akagi.Utils.Extensions;

namespace Akagi.Communication.Commands;

internal class GetUptimeCommand : TextCommand
{
    public override string Name => "/uptime";

    public override string Description => "Retrieves the uptime of the application.";

    private readonly ApplicationInformation _applicationInformation;

    public GetUptimeCommand(ApplicationInformation applicationInformation)
    {
        _applicationInformation = applicationInformation;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        TimeSpan uptime = _applicationInformation.Uptime;

        string message = $"Application has been running for " +
            $"{uptime.Days.Pluralize("day", "days")}, " +
            $"{uptime.Hours.Pluralize("hour", "hours")}, " +
            $"{uptime.Minutes.Pluralize("minute", "minutes")}, and " +
            $"{uptime.Seconds.Pluralize("second", "seconds")}.";

        return Communicator.SendMessage(context.User, message);
    }
}
