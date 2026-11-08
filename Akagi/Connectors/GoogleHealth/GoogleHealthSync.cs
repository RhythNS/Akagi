using Akagi.Life;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace Akagi.Connectors.GoogleHealth;

/// <summary>
/// What a sync imported. <paramref name="NotShared"/> lists data the user did not grant access to, e.g. "sleep".
/// </summary>
internal record GoogleHealthSyncResult(int SleepDays, int StepDays, int HeartRateDays, string NotShared = "", int ExerciseSessions = 0)
{
    public override string ToString()
    {
        string text = $"Synced sleep for {SleepDays} day(s), steps for {StepDays} day(s), resting heart rate for {HeartRateDays} day(s) " +
                      $"and {ExerciseSessions} exercise session(s) from Google Health.";
        return NotShared.Length == 0 ? text : $"{text} Not shared with Akagi: {NotShared} (run /health link again to allow it).";
    }
}

internal interface IGoogleHealthSync
{
    /// <summary>
    /// Pulls the last days of sleep, steps, resting heart rate and exercise sessions into the user's life profile and saves both
    /// the profile and the account. Returns null when the user has not linked Google Health.
    /// </summary>
    public Task<GoogleHealthSyncResult?> SyncUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether an account is due for a background sync: more often in the user's local morning.
    /// </summary>
    public bool IsDue(GoogleHealthAccount account, LifeProfile profile, DateTime nowUtc);
}

internal class GoogleHealthSync : IGoogleHealthSync
{
    /// <summary>
    /// Sleep pages hold at most 25 sessions; a few days never need more than a couple.
    /// </summary>
    public const int MaxPages = 10;

    private readonly IGoogleHealthClient _client;
    private readonly IGoogleHealthAccountDatabase _accounts;
    private readonly ILifeProfileDatabase _profiles;
    private readonly IOptionsMonitor<GoogleHealthOptions> _options;

    public GoogleHealthSync(IGoogleHealthClient client, IGoogleHealthAccountDatabase accounts, ILifeProfileDatabase profiles, IOptionsMonitor<GoogleHealthOptions> options)
    {
        _client = client;
        _accounts = accounts;
        _profiles = profiles;
        _options = options;
    }

    public async Task<GoogleHealthSyncResult?> SyncUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        GoogleHealthAccount? account = await _accounts.GetForUser(userId);
        if (account is not { IsConnected: true })
        {
            return null;
        }

        LifeProfile profile = await _profiles.GetOrCreate(userId);
        DateTime localToday = profile.ToLocal(DateTime.UtcNow).Date;
        DateTime first = localToday.AddDays(-Math.Max(0, _options.CurrentValue.DaysToSync - 1));
        Func<GoogleHealthAccount, Task> persist = _accounts.SaveDocumentAsync;
        List<string> notShared = [];

        try
        {
            int sleepDays = 0;
            int stepDays = 0;
            int heartDays = 0;
            int exerciseSessions = 0;

            if (await TryFetch(account, GoogleHealthOptions.SleepScope, "sleep", notShared, async () =>
                await GetPagesAsync(account, "users/me/dataTypes/sleep/dataPoints:reconcile",
                    CivilFilter("sleep.interval.civil_end_time", first, localToday), 25, persist, cancellationToken)) is { } sleep)
            {
                sleepDays = GoogleHealthMapper.ApplySleep(profile, sleep);
            }

            if (await TryFetch(account, GoogleHealthOptions.ActivityScope, "steps", notShared, async () =>
                new List<string> { await _client.PostJsonAsync(account, "users/me/dataTypes/steps/dataPoints:dailyRollUp",
                    DailyRollUpBody(first, localToday), persist, cancellationToken) }) is { } steps)
            {
                stepDays = GoogleHealthMapper.ApplySteps(profile, steps[0]);
            }

            DateTime exerciseFirst = localToday.AddDays(-Math.Max(0, _options.CurrentValue.ExerciseDaysToSync - 1));
            if (await TryFetch(account, GoogleHealthOptions.ActivityScope, "exercise", notShared, async () =>
                await GetPagesAsync(account, "users/me/dataTypes/exercise/dataPoints",
                    CivilFilter("exercise.interval.civil_start_time", exerciseFirst, localToday), 25, persist, cancellationToken)) is { } exercise)
            {
                exerciseSessions = GoogleHealthMapper.ApplyExercise(profile, exercise, exerciseFirst, localToday);
            }

            if (await TryFetch(account, GoogleHealthOptions.MetricsScope, "resting heart rate", notShared, async () =>
                await GetPagesAsync(account, "users/me/dataTypes/daily-resting-heart-rate/dataPoints",
                    CivilFilter("daily_resting_heart_rate.date", first, localToday), 0, persist, cancellationToken)) is { } heart)
            {
                heartDays = GoogleHealthMapper.ApplyRestingHeartRate(profile, heart);
            }

            await _profiles.SaveDocumentAsync(profile);
            account.LastSyncUtc = DateTime.UtcNow;
            account.LastError = string.Empty;
            await _accounts.SaveDocumentAsync(account);
            return new GoogleHealthSyncResult(sleepDays, stepDays, heartDays, string.Join(", ", notShared), exerciseSessions);
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

    /// <summary>
    /// Runs a fetch unless the user withheld its scope; a scope refused by the API is noted the same way.
    /// </summary>
    private static async Task<List<string>?> TryFetch(GoogleHealthAccount account, string scope, string label, List<string> notShared, Func<Task<List<string>>> fetch)
    {
        if (!account.HasScope(scope))
        {
            notShared.Add(label);
            return null;
        }
        try
        {
            return await fetch();
        }
        catch (GoogleHealthMissingScopeException)
        {
            notShared.Add(label);
            return null;
        }
    }

    private async Task<List<string>> GetPagesAsync(GoogleHealthAccount account, string path, string filter, int pageSize,
        Func<GoogleHealthAccount, Task> persist, CancellationToken cancellationToken)
    {
        List<string> pages = [];
        string? token = null;
        do
        {
            string query = "filter=" + Uri.EscapeDataString(filter);
            if (pageSize > 0)
            {
                query += "&pageSize=" + pageSize.ToString(CultureInfo.InvariantCulture);
            }
            if (token != null)
            {
                query += "&pageToken=" + Uri.EscapeDataString(token);
            }
            string page = await _client.GetAsync(account, path + "?" + query, persist, cancellationToken);
            pages.Add(page);
            token = GoogleHealthMapper.NextPageToken(page);
        }
        while (token != null && pages.Count < MaxPages);
        return pages;
    }

    /// <summary>
    /// A civil date range in the user's local calendar, last day included: field >= "first" AND field &lt; "last + 1".
    /// </summary>
    internal static string CivilFilter(string field, DateTime first, DateTime last) =>
        $"{field} >= \"{first:yyyy-MM-dd}\" AND {field} < \"{last.AddDays(1):yyyy-MM-dd}\"";

    /// <summary>
    /// Daily step totals from the first day's midnight to the end of the last day, one window per day.
    /// </summary>
    internal static string DailyRollUpBody(DateTime first, DateTime last) => JsonSerializer.Serialize(new
    {
        range = new
        {
            start = new
            {
                date = new { year = first.Year, month = first.Month, day = first.Day },
                time = new { hours = 0, minutes = 0, seconds = 0, nanos = 0 }
            },
            end = new
            {
                date = new { year = last.Year, month = last.Month, day = last.Day },
                time = new { hours = 23, minutes = 59, seconds = 59, nanos = 0 }
            }
        },
        windowSizeDays = 1
    });

    public bool IsDue(GoogleHealthAccount account, LifeProfile profile, DateTime nowUtc)
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
        GoogleHealthOptions options = _options.CurrentValue;
        int minutes = localHour is >= 5 and < 11 ? options.MorningSyncIntervalMinutes : options.SyncIntervalMinutes;
        return nowUtc - account.LastSyncUtc.Value >= TimeSpan.FromMinutes(Math.Max(5, minutes));
    }
}

/// <summary>
/// Keeps linked users' sleep and activity fresh, so routines and tone react to a bad night without a manual export.
/// </summary>
internal class GoogleHealthSyncService : BackgroundService
{
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<GoogleHealthSyncService> _logger;

    public GoogleHealthSyncService(IServiceProvider services, ILogger<GoogleHealthSyncService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_services.GetRequiredService<IGoogleHealthClient>().IsConfigured)
        {
            _logger.LogInformation("Google Health is not configured; background sync disabled");
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
                _logger.LogError(ex, "Google Health sync pass failed");
            }
        }
    }

    private async Task SyncDueAccountsAsync(CancellationToken cancellationToken)
    {
        IGoogleHealthSync sync = _services.GetRequiredService<IGoogleHealthSync>();
        ILifeProfileDatabase profiles = _services.GetRequiredService<ILifeProfileDatabase>();
        foreach (GoogleHealthAccount account in await _services.GetRequiredService<IGoogleHealthAccountDatabase>().GetConnected())
        {
            LifeProfile profile = await profiles.GetOrCreate(account.UserId);
            if (!sync.IsDue(account, profile, DateTime.UtcNow))
            {
                continue;
            }
            try
            {
                GoogleHealthSyncResult? result = await sync.SyncUserAsync(account.UserId, cancellationToken);
                _logger.LogInformation("Google Health sync for user {UserId}: {Result}", account.UserId, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Google Health sync failed for user {UserId}", account.UserId);
            }
        }
    }
}
