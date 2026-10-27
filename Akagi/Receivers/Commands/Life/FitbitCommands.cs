using Akagi.Connectors.Fitbit;
using Akagi.Flow;
using Akagi.Life;
using Akagi.Life.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands.Life;

internal class SyncHealthCommand : LifeCommand
{
    private readonly IFitbitSync? _sync;

    public SyncHealthCommand()
    {
    }

    internal SyncHealthCommand(IFitbitSync sync)
    {
        _sync = sync;
    }

    public override string Name => "SyncHealth";

    public override string Description =>
        "Fetch the latest sleep (with stages), steps and resting heart rate from the user's Fitbit right now, " +
        "e.g. in the morning before asking how they slept. Falls back to what is already known when Fitbit is not linked.";

    public override Argument[] GetDefaultArguments() => [];

    public override async Task<Command[]> Execute(Context context)
    {
        IFitbitSync? sync = _sync ?? (Globals.IsInitialized ? Globals.Instance.ServiceProvider.GetService<IFitbitSync>() : null);
        string status;
        if (sync == null)
        {
            status = "Fitbit sync is not available on this server.";
        }
        else
        {
            try
            {
                FitbitSyncResult? result = await sync.SyncUserAsync(context.User.Id!);
                status = result?.ToString() ?? "The user has not linked Fitbit (they can with /fitbit link).";
            }
            catch (Exception ex) when (ex is FitbitAuthException or HttpRequestException)
            {
                status = $"Fitbit sync failed: {ex.Message}";
            }
        }

        LifeProfile profile = await LoadProfile(context);
        HealthSummary summary = HealthSummary.Create(profile, DateTime.UtcNow);
        return Reply(context, summary.HasData ? status + Environment.NewLine + summary.Describe() : status + " No health data yet.");
    }
}
