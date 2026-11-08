using Akagi.Connectors.TimeTracker;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Health;
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
        int count = Math.Clamp(GetIntArgument("EntriesPerTracker") ?? 3, 1, 20);
        string overview = await TimeTrackerLookup.RunAsync(context, (connector, config, profile) =>
            TimeTrackerWorkLog.DescribeAsync(connector, config, profile, count));
        return Reply(context, overview);
    }
}

/// <summary>
/// His workouts: the watch's exercise sessions from Google Health together with the interval timer workouts that
/// Akagi.Web logs to the "Workouts" tracker.
/// </summary>
internal class WorkoutHistoryCommand : LifeCommand
{
    public override string Name => "WorkoutHistory";

    public override string Description =>
        "Look up the user's workouts over more days: the exercise sessions his watch recorded (Google Health: type, time, " +
        "active minutes, average heart rate, heart rate zones, Active Zone Minutes, calories) merged with the workouts he did " +
        "with his interval timer (routine, rounds done of planned, finished or stopped early). SyncHealth and HealthStatus " +
        "already show the last 7 days.";

    public override Argument[] GetDefaultArguments() =>
    [
        Integer("Days", "How many days back to look, today included (default 14, at most 90)."),
        Text("Tracker", "Only when his timer workouts go to a tracker of his own: its name (default \"Workouts\").")
    ];

    public override async Task<Command[]> Execute(Context context)
    {
        int days = Math.Clamp(GetIntArgument("Days") ?? 14, 1, 90);
        LifeProfile profile = await LoadProfile(context);
        return Reply(context, await ActivityLookup.DescribeAsync(context, profile, days, GetArgument("Tracker")));
    }
}

/// <summary>
/// The <see cref="ActivityReport"/> for a user: the watch sessions in the profile and the timer workouts read from
/// the TimeTracker. When the TimeTracker cannot be read the report still has the watch, with a note why.
/// </summary>
internal static class ActivityLookup
{
    public const int ReportDays = 7;

    public static async Task<string> DescribeAsync(Context context, LifeProfile profile, int days = ReportDays, string? tracker = null)
    {
        (List<TimeTrackerWorkouts.Session>? sessions, string? problem) = await TimeTrackerLookup.TryAsync(context, async (connector, config, p) =>
        {
            (List<TimeTrackerWorkouts.Session>? loaded, _, string? missing) = await TimeTrackerWorkouts.LoadAsync(connector, config, p, tracker);
            return (loaded, missing);
        });
        return ActivityReport.Describe(profile, sessions, problem, DateTime.UtcNow, days);
    }
}

/// <summary>
/// Runs a read against the user's TimeTracker, or says why it cannot be reached.
/// </summary>
internal static class TimeTrackerLookup
{
    public static async Task<string> RunAsync(Context context, Func<ITimeTrackerConnector, TimeTrackerUserConfig, LifeProfile, Task<string>> read)
    {
        (string? result, string? problem) = await TryAsync(context, async (connector, config, profile) => ((string?)await read(connector, config, profile), (string?)null));
        return result ?? problem ?? string.Empty;
    }

    /// <summary>
    /// The read's result, or null and the reason it could not run (no linked account, not configured, unreachable).
    /// </summary>
    public static async Task<(T? Result, string? Problem)> TryAsync<T>(Context context,
        Func<ITimeTrackerConnector, TimeTrackerUserConfig, LifeProfile, Task<(T? Result, string? Problem)>> read) where T : class
    {
        string? googleId = context.User.GoogleUser?.Id;
        if (string.IsNullOrEmpty(googleId))
        {
            return (null, "The user has not linked the TimeTracker web app (no Google account connected).");
        }
        if (!Globals.IsInitialized)
        {
            return (null, "TimeTracker is not available.");
        }

        using IServiceScope scope = Globals.Instance.ServiceProvider.CreateScope();
        ITimeTrackerConnector connector = scope.ServiceProvider.GetRequiredService<ITimeTrackerConnector>();
        if (!connector.IsConfigured)
        {
            return (null, "TimeTracker is not configured on the server.");
        }

        string userId = context.User.Id ?? throw new InvalidOperationException("User has no id.");
        LifeProfile profile = await context.DatabaseFactory.GetDatabase<ILifeProfileDatabase>().GetOrCreate(userId);
        try
        {
            return await read(connector, new TimeTrackerUserConfig { GoogleId = googleId }, profile);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException || ex.Message.StartsWith("Request failed"))
        {
            return (null, $"TimeTracker could not be reached: {ex.Message}");
        }
    }
}
