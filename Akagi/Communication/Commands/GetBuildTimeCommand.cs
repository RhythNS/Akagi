using Akagi.Utils;
using System.Text;

namespace Akagi.Communication.Commands;

internal class GetBuildTimeCommand : TextCommand
{
    public override string Name => "/getBuildTime";

    public override string Description => "Retrieves the build time of the application.";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        DateTime? buildTime = EmbeddedBuildTimeReader.GetEmbeddedBuildTimestampUtc(typeof(GetBuildTimeCommand).Assembly);

        string response;
        if (buildTime.HasValue)
        {
            TimeSpan timeSpan = DateTime.UtcNow - buildTime.Value;
            StringBuilder sb = new();
            sb.Append($"The application was built on {buildTime.Value:yyyy-MM-dd HH:mm:ss} UTC");
            if (timeSpan.TotalDays >= 1)
            {
                sb.Append($" (~{timeSpan.Days} {(timeSpan.Days == 1 ? "day" : "days")} ago).");
            }
            else if (timeSpan.TotalHours >= 1)
            {
                sb.Append($" (~{timeSpan.Hours} {(timeSpan.Hours == 1 ? "hour" : "hours")} ago).");
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                sb.Append($" (~{timeSpan.Minutes} {(timeSpan.Minutes == 1 ? "minute" : "minutes")} ago).");
            }
            else
            {
                sb.Append(" (just a moment ago).");
            }
            response = sb.ToString();
        }
        else
        {
            response = "Build time information is not available.";
        }

        return Communicator.SendMessage(context.User, response);
    }
}
