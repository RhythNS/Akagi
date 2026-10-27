namespace Akagi.Life.Health;

/// <summary>
/// Health data for one local calendar day. Sleep belongs to the day the user woke up.
/// </summary>
internal class HealthDay
{
    public string Date { get; set; } = string.Empty;
    public int? SleepMinutes { get; set; }
    public DateTime? SleepStartUtc { get; set; }
    public DateTime? SleepEndUtc { get; set; }
    /// <summary>Sleep stages from a tracker; null when only the duration is known.</summary>
    public int? DeepSleepMinutes { get; set; }
    public int? RemSleepMinutes { get; set; }
    public int? LightSleepMinutes { get; set; }
    public int? AwakeMinutes { get; set; }
    /// <summary>Percentage of time in bed spent asleep, as reported by the tracker.</summary>
    public int? SleepEfficiency { get; set; }
    /// <summary>Daytime sleep besides the main night.</summary>
    public int? NapMinutes { get; set; }
    public int? Steps { get; set; }
    public int? RestingHeartRate { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

internal class HealthSummary
{
    public const int TargetSleepMinutes = 450;
    public const int ShortSleepMinutes = 360;

    public string Today { get; init; } = string.Empty;
    public HealthDay? LastSleep { get; init; }
    public bool LastSleepIsLastNight { get; init; }
    public double? AverageSleepMinutes { get; init; }
    public int SleepDebtMinutes { get; init; }
    public int? StepsToday { get; init; }
    public int? StepsYesterday { get; init; }
    public double? AverageSteps { get; init; }
    public int? RestingHeartRate { get; init; }

    public bool HasData => LastSleep != null || StepsToday.HasValue || StepsYesterday.HasValue;

    public bool SleptShortLastNight => LastSleepIsLastNight && LastSleep?.SleepMinutes is < ShortSleepMinutes;

    public static HealthSummary Create(LifeProfile profile, DateTime nowUtc)
    {
        DateTime localNow = profile.ToLocal(nowUtc);
        string today = LifeClock.DateKey(localNow);
        string yesterday = LifeClock.DateKey(localNow.AddDays(-1));
        string weekStart = LifeClock.DateKey(localNow.AddDays(-6));

        List<HealthDay> week = [.. profile.HealthDays.Where(d => string.CompareOrdinal(d.Date, weekStart) >= 0 && string.CompareOrdinal(d.Date, today) <= 0)];
        List<HealthDay> sleepDays = [.. week.Where(d => d.SleepMinutes.HasValue)];
        List<HealthDay> stepDays = [.. week.Where(d => d.Steps.HasValue && d.Date != today)];

        HealthDay? lastSleep = profile.HealthDays
            .Where(d => d.SleepMinutes.HasValue && string.CompareOrdinal(d.Date, today) <= 0)
            .MaxBy(d => d.Date);

        return new HealthSummary
        {
            Today = today,
            LastSleep = lastSleep,
            LastSleepIsLastNight = lastSleep?.Date == today,
            AverageSleepMinutes = sleepDays.Count > 0 ? sleepDays.Average(d => d.SleepMinutes!.Value) : null,
            SleepDebtMinutes = sleepDays.Sum(d => Math.Max(0, TargetSleepMinutes - d.SleepMinutes!.Value)),
            StepsToday = profile.HealthDays.FirstOrDefault(d => d.Date == today)?.Steps,
            StepsYesterday = profile.HealthDays.FirstOrDefault(d => d.Date == yesterday)?.Steps,
            AverageSteps = stepDays.Count > 0 ? stepDays.Average(d => d.Steps!.Value) : null,
            RestingHeartRate = week.Where(d => d.RestingHeartRate.HasValue).MaxBy(d => d.Date)?.RestingHeartRate
        };
    }

    public string Describe()
    {
        List<string> parts = [];
        if (LastSleep?.SleepMinutes is int minutes)
        {
            string when = LastSleepIsLastNight ? "last night" : $"on the night before {LastSleep.Date}";
            string sleep = $"Sleep {when}: {LifeClock.FormatDuration(TimeSpan.FromMinutes(minutes))}";
            if (AverageSleepMinutes.HasValue)
            {
                sleep += $" (7-day average {LifeClock.FormatDuration(TimeSpan.FromMinutes(AverageSleepMinutes.Value))}";
                sleep += SleepDebtMinutes >= 60 ? $", sleep debt {LifeClock.FormatDuration(TimeSpan.FromMinutes(SleepDebtMinutes))})" : ")";
            }
            if (SleptShortLastNight)
            {
                sleep += " - short night";
            }
            parts.Add(sleep);
            string quality = DescribeQuality(LastSleep);
            if (quality.Length > 0)
            {
                parts.Add(quality);
            }
        }
        if (StepsToday.HasValue || StepsYesterday.HasValue)
        {
            string steps = "Steps:";
            if (StepsToday.HasValue)
            {
                steps += $" {StepsToday.Value:N0} today";
            }
            if (StepsYesterday.HasValue)
            {
                steps += $"{(StepsToday.HasValue ? "," : string.Empty)} {StepsYesterday.Value:N0} yesterday";
            }
            if (AverageSteps.HasValue)
            {
                steps += $" (7-day average {AverageSteps.Value:N0})";
            }
            parts.Add(steps);
        }
        if (RestingHeartRate.HasValue)
        {
            parts.Add($"Resting heart rate: {RestingHeartRate} bpm");
        }
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// Stages, efficiency and naps of one night, e.g. "Deep 1h05m, REM 1h30m, awake 40m, efficiency 84%".
    /// </summary>
    internal static string DescribeQuality(HealthDay day)
    {
        List<string> parts = [];
        if (day.DeepSleepMinutes is int deep)
        {
            parts.Add($"deep {LifeClock.FormatDuration(TimeSpan.FromMinutes(deep))}");
        }
        if (day.RemSleepMinutes is int rem)
        {
            parts.Add($"REM {LifeClock.FormatDuration(TimeSpan.FromMinutes(rem))}");
        }
        if (day.AwakeMinutes is int awake)
        {
            parts.Add($"awake {LifeClock.FormatDuration(TimeSpan.FromMinutes(awake))}");
        }
        if (day.SleepEfficiency is int efficiency)
        {
            parts.Add($"efficiency {efficiency}%");
        }
        if (day.NapMinutes is int nap and > 0)
        {
            parts.Add($"napped {LifeClock.FormatDuration(TimeSpan.FromMinutes(nap))}");
        }
        return parts.Count == 0 ? string.Empty : "Sleep quality: " + string.Join(", ", parts);
    }

    /// <summary>
    /// Little deep sleep, many wake-ups or low efficiency: a night that was long enough can still be a bad one.
    /// </summary>
    public bool LastNightWasRestless =>
        LastSleepIsLastNight && LastSleep != null
        && (LastSleep.SleepEfficiency is < 80 || LastSleep.AwakeMinutes is > 60 || LastSleep.DeepSleepMinutes is < 40);
}
