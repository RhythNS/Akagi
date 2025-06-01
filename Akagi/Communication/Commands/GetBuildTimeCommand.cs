using Akagi.Utils;

namespace Akagi.Communication.Commands;

internal class GetBuildTimeCommand : TextCommand
{
    public override string Name => "/getBuildTime";

    public override string Description => "Retrieves the build time of the application.";

    public override Task ExecuteAsync(Context context, string[] args)
    {
        DateTime? buildTime = EmbeddedBuildTimeReader.GetEmbeddedBuildTimestampUtc(typeof(GetBuildTimeCommand).Assembly);

        string response = buildTime.HasValue
            ? $"The application was built on: {buildTime.Value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)} UTC"
            : "Build time information is not available.";
        return Communicator.SendMessage(context.User, response);
    }
}
