using Akagi.Life;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Akagi.Connectors.Fitbit;

internal record FitbitSyncResult(int SleepDays, int StepDays, int HeartRateDays)
{
    public override string ToString() =>
        $"Synced sleep for {SleepDays} day(s), steps for {StepDays} day(s) and resting heart rate for {HeartRateDays} day(s) from Fitbit.";
}

internal interface IFitbitSync
{
    /// <summary>
    /// Pulls the last days of sleep, steps and resting heart rate into the user's life profile and saves both
    /// the profile and the account. Returns null when the user has not linked Fitbit.
    /// </summary>
    public Task<FitbitSyncResult?> SyncUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an account is due for a background sync: more often in the user's local morning.
    /// </summary>
    public bool IsDue(FitbitAccount account, LifeProfile profile, DateTime nowUtc);
}

internal class FitbitSync : IFitbitSync
{
    private readonly IFitbitClient _client;
    private readonly IFitbitAccountDatabase _accounts;
    private readonly ILifeProfileDatabase _profiles;
    private readonly IOptionsMonitor<FitbitOptions> _options;

    public FitbitSync(IFitbitClient client, IFitbitAccountDatabase accounts, ILifeProfileDatabase profiles, IOptionsMonitor<FitbitOptions> options)
    {
        _client = client;
        _accounts = accounts;
        _profiles = profiles;
        _options = options;
    }

    public async Task<FitbitSyncResult?> SyncUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        FitbitAccount? account = await _accounts.GetForUser(userId);
        if (account is not { IsConnected: true })
        {
            return null;
        }

        LifeProfile profile = await _profiles.GetOrCreate(userId);
        DateTime localToday = profile.ToLocal(DateTime.UtcNow).Date;
        string end = localToday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string start = localToday.AddDays(-Math.Max(0, _options.CurrentValue.DaysToSync - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Func<FitbitAccount, Task> persist = _accounts.SaveDocumentAsync;

        try
        {
            string sleep = await _client.GetAsync(account, $"1.2/user/-/sleep/date/{start}/{end}.json", persist, cancellationToken);
            string steps = await _client.GetAsync(account, $"1/user/-/activities/steps/date/{start}/{end}.json", persist, cancellationToken);
            string heart = await _client.GetAsync(account, $"1/user/-/activities/heart/date/{start}/{end}.json", persist, cancellationToken);

            FitbitSyncResult result = new(
                FitbitHealthMapper.ApplySleep(profile, sleep),
                FitbitHealthMapper.ApplySteps(profile, steps),
                FitbitHealthMapper.ApplyRestingHeartRate(profile, heart));

            await _profiles.SaveDocumentAsync(profile);
            account.LastSyncUtc = DateTime.UtcNow;
            account.LastError = string.Empty;
            await _accounts.SaveDocumentAsync(account);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Remember the failure so the next background pass waits a full interval instead of hammering the API
            account.LastSyncUtc = DateTime.UtcNow;
            account.LastError = ex.Message;
            await _accounts.SaveDocumentAsync(account);
            throw;
        }
    }

    public bool IsDue(FitbitAccount account, LifeProfile profile, DateTime nowUtc)
    {
        if (!account.IsConnected)
        {
            return false;
        }
        if (account.LastSyncUtc == null)
        {
            return true;
        }

        int localHour = profile.ToLocal(nowUtc).Hour;
        FitbitOptions options = _options.CurrentValue;
        int minutes = localHour is >= 5 and < 11 ? options.MorningSyncIntervalMinutes : options.SyncIntervalMinutes;
        return nowUtc - account.LastSyncUtc.Value >= TimeSpan.FromMinutes(Math.Max(5, minutes));
    }
}

/// <summary>
/// Keeps linked users' sleep and activity fresh, so routines and tone react to a bad night without a manual export.
/// </summary>
internal class FitbitSyncService : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<FitbitSyncService> _logger;

    public FitbitSyncService(IServiceProvider services, ILogger<FitbitSyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_services.GetRequiredService<IFitbitClient>().IsConfigured)
        {
            _logger.LogInformation("Fitbit is not configured; background sync disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Tick, stoppingToken);
                await SyncDueAccountsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fitbit sync pass failed");
            }
        }
    }

    private async Task SyncDueAccountsAsync(CancellationToken cancellationToken)
    {
        IFitbitSync sync = _services.GetRequiredService<IFitbitSync>();
        ILifeProfileDatabase profiles = _services.GetRequiredService<ILifeProfileDatabase>();
        foreach (FitbitAccount account in await _services.GetRequiredService<IFitbitAccountDatabase>().GetConnected())
        {
            LifeProfile profile = await profiles.GetOrCreate(account.UserId);
            if (!sync.IsDue(account, profile, DateTime.UtcNow))
            {
                continue;
            }
            try
            {
                FitbitSyncResult? result = await sync.SyncUserAsync(account.UserId, cancellationToken);
                _logger.LogInformation("Fitbit sync for user {UserId}: {Result}", account.UserId, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Fitbit sync failed for user {UserId}", account.UserId);
            }
        }
    }
}
