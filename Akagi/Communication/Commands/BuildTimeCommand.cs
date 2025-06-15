using Akagi.Utils;
using Akagi.Utils.Extensions;

namespace Akagi.Communication.Commands;

internal class BuildTimeCommand : TextCommand
{
    public override string Name => "/buildTime";

    public override string Description => "Retrieves the build time of the application.";

    private readonly ApplicationInformation applicationInformation;

    public BuildTimeCommand(ApplicationInformation applicationInformation)
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
            response = $"The application was built {timeSpan.Stringify()} ago.";
        }

        return Communicator.SendMessage(context.User, response);
    }
}
