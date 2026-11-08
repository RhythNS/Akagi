using Akagi.Connectors.GoogleHealth;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands.Life;

internal class SyncHealthCommand : LifeCommand
{
    private readonly IGoogleHealthSync? _sync;

    public SyncHealthCommand()
    {
    }

    internal SyncHealthCommand(IGoogleHealthSync sync)
    {
        _sync = sync;
    }

    public override string Name => "SyncHealth";

    public override string Description =>
        "Fetch the latest sleep (with stages), steps, resting heart rate and exercise sessions from the user's watch through Google " +
        "Health right now, e.g. in the morning before asking how they slept, and show them with the last 7 days of workouts " +
        "(watch sessions merged with his interval timer workouts). Falls back to what is already known when Google Health is not linked.";

    public override Argument[] GetDefaultArguments() => [];

    public override async Task<Command[]> Execute(Context context)
    {
        IGoogleHealthSync? sync = _sync ?? (Globals.IsInitialized ? Globals.Instance.ServiceProvider.GetService<IGoogleHealthSync>() : null);
        string status;
        if (sync == null)
        {
            status = "Health sync is not available on this server.";
        }
        else
        {
            try
            {
                GoogleHealthSyncResult? result = await sync.SyncUserAsync(context.User.Id!);
                status = result?.ToString() ?? "The user has not linked Google Health (they can with /health link).";
            }
            catch (Exception ex) when (ex is GoogleHealthAuthException or HttpRequestException)
            {
                status = $"Health sync failed: {ex.Message}";
            }
        }

        LifeProfile profile = await LoadProfile(context);
        HealthSummary summary = HealthSummary.Create(profile, DateTime.UtcNow);
        string health = summary.HasData ? status + Environment.NewLine + summary.Describe(withExercise: false) : status + " No sleep, step or heart rate data yet.";
        return Reply(context, health + Environment.NewLine + await ActivityLookup.DescribeAsync(context, profile));
    }
}
