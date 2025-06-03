using Akagi.Utils;
using Akagi.Utils.Extensions;
using System.Text;

namespace Akagi.Communication.Commands;

internal class GetBuildTimeCommand : TextCommand
{
    public override string Name => "/buildTime";

    public override string Description => "Retrieves the build time of the application.";

    private readonly ApplicationInformation applicationInformation;

    public GetBuildTimeCommand(ApplicationInformation applicationInformation)
    {
        this.applicationInformation = applicationInformation;
    }

    public override Task ExecuteAsync(Context context, string[] args)
    {
        DateTime? buildTime = applicationInformation.BuildTimestampUtc;

        string response;
        if (!buildTime.HasValue)
        {
            response = "Build time information is not available.";
        }
        else
        {
            TimeSpan timeSpan = DateTime.UtcNow - buildTime.Value;
            StringBuilder sb = new();
            sb.Append($"The application was built on {buildTime.Value:yyyy-MM-dd HH:mm:ss} UTC");
            if (timeSpan.TotalDays >= 1)
            {
                sb.Append($" (~{timeSpan.Days.Pluralize("day", "days")} ago).");
            }
            else if (timeSpan.TotalHours >= 1)
            {
                sb.Append($" (~{timeSpan.Hours.Pluralize("hour", "hours")} ago).");
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                sb.Append($" (~{timeSpan.Minutes.Pluralize("minute", "minutes")} ago).");
            }
            else
            {
                sb.Append(" (just a moment ago).");
            }
            response = sb.ToString();
        }

        return Communicator.SendMessage(context.User, response);
    }
}
