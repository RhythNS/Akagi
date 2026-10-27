using Akagi.Connectors.TimeTracker;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands.Life;

/// <summary>
/// Pushes finished work sessions to Akagi.Web's TimeTracker when the user has linked a Google account.
/// Work tracking keeps working locally when the TimeTracker is not reachable.
/// </summary>
internal static class WorkSync
{
    public static async Task<string> TrySync(Context context, LifeProfile profile, WorkSession session)
    {
        string? googleId = context.User.GoogleUser?.Id;
        if (string.IsNullOrEmpty(googleId) || !Globals.IsInitialized)
        {
            return string.Empty;
        }

        try
        {
            using IServiceScope scope = Globals.Instance.ServiceProvider.CreateScope();
            ITimeTrackerConnector connector = scope.ServiceProvider.GetRequiredService<ITimeTrackerConnector>();
            if (!connector.IsConfigured)
            {
                return string.Empty;
            }

            await TimeTrackerWorkLog.SyncAsync(connector, new TimeTrackerUserConfig { GoogleId = googleId }, session, DateTime.UtcNow);
            return $"Logged to TimeTracker ({TimeTrackerWorkLog.DefinitionName}).";
        }
        catch (Exception ex)
        {
            return $"(TimeTracker sync failed: {ex.Message})";
        }
    }
}

internal class TimeTrackerOverviewCommand : LifeCommand
{
    public override string Name => "TimeTrackerOverview";

    public override string Description =>
        "Look up everything the user tracks in the TimeTracker web app (habits, hours, custom trackers) with the latest entries.";

    public override Argument[] GetDefaultArguments() =>
    [
        Integer("EntriesPerTracker", "How many recent entries to show per tracker (default 3).")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        string? googleId = context.User.GoogleUser?.Id;
        if (string.IsNullOrEmpty(googleId))
        {
            return Reply(context, "The user has not linked the TimeTracker web app (no Google account connected).");
        }
        if (!Globals.IsInitialized)
        {
            return Reply(context, "TimeTracker is not available.");
        }

        using IServiceScope scope = Globals.Instance.ServiceProvider.CreateScope();
        ITimeTrackerConnector connector = scope.ServiceProvider.GetRequiredService<ITimeTrackerConnector>();
        if (!connector.IsConfigured)
        {
            return Reply(context, "TimeTracker is not configured on the server.");
        }

        LifeProfile profile = await LoadProfile(context);
        int count = Math.Clamp(GetIntArgument("EntriesPerTracker") ?? 3, 1, 20);
        string overview = await TimeTrackerWorkLog.DescribeAsync(connector, new TimeTrackerUserConfig { GoogleId = googleId }, profile, count);
        return Reply(context, overview);
    }
}
