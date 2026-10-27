using Akagi.Connectors.TimeTracker;
using Akagi.Life;

namespace Akagi.Communication.Commands.Web;

internal class GetTimeTrakerDefinitionCommand : WebCommand
{
    public override string Name => "/GetTimeTrackerDefinition";

    public override string Description => "Lists your TimeTracker definitions with their latest entries. Usage: /GetTimeTrackerDefinition [entriesPerTracker]";

    private readonly ITimeTrackerConnector _connector;

    public GetTimeTrakerDefinitionCommand(ITimeTrackerConnector connector)
    {
        _connector = connector;
    }

    public override async Task<CommandResult> ExecuteAsync(Context context, string[] args)
    {
        string? googleId = await TryGetGoogleIdOrPrintError(context);
        if (googleId == null)
        {
            return CommandResult.Fail(UserNotConfiguredError);
        }
        if (!_connector.IsConfigured)
        {
            await Communicator.SendMessage(context.User, "TimeTracker is not configured on the server.");
            return CommandResult.Fail("TimeTracker not configured.");
        }

        int entries = 3;
        if (args.Length > 0 && (!int.TryParse(args[0], out entries) || entries < 0))
        {
            await Communicator.SendMessage(context.User, "Usage: /GetTimeTrackerDefinition [entriesPerTracker]");
            return CommandResult.Fail("Invalid entry count.");
        }

        LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetForUser(context.User.Id!)
            ?? new LifeProfile { UserId = context.User.Id! };

        string overview = await TimeTrackerWorkLog.DescribeAsync(_connector, new TimeTrackerUserConfig { GoogleId = googleId }, profile, entries);
        await Communicator.SendMessage(context.User, overview);
        return CommandResult.Ok;
    }
}
