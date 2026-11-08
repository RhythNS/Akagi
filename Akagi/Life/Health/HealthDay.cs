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
    /// <summary>Workouts the watch recorded that started on this day, started by hand or detected.</summary>
    public List<HealthExercise> Exercises { get; set; } = [];
    public string Source { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One exercise session from the watch (Google Health "exercise"), with what it measured during it.
/// </summary>
internal class HealthExercise
{
    /// <summary>The data point's name in Google Health, so a later sync replaces instead of duplicating it.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Google's type, e.g. JUMPING_ROPE or WALKING.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>What the watch calls it, e.g. "Skipping rope".</summary>
    public string Name { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    /// <summary>Time moving, without pauses.</summary>
    public int ActiveSeconds { get; set; }
    public int? Calories { get; set; }
    public int? Steps { get; set; }
    public double? DistanceMeters { get; set; }
    public int? AverageHeartRate { get; set; }
    public int? ActiveZoneMinutes { get; set; }
    /// <summary>Time in each heart rate zone.</summary>
    public int? LightSeconds { get; set; }
    public int? ModerateSeconds { get; set; }
    public int? VigorousSeconds { get; set; }
    public int? PeakSeconds { get; set; }
    /// <summary>Recognised by the watch on its own instead of started by him.</summary>
    public bool AutoDetected { get; set; }
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
    /// <summary>The watch's exercise sessions of the last 7 days, oldest first.</summary>
    public IReadOnlyList<HealthExercise> RecentExercises { get; init; } = [];
    public DateTime? LatestExerciseLocalStart { get; init; }
    public DateTime LocalToday { get; init; }

    public bool HasData => LastSleep != null || StepsToday.HasValue || StepsYesterday.HasValue || RecentExercises.Count > 0;

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

        List<HealthExercise> exercises = [.. week.SelectMany(d => d.Exercises).OrderBy(e => e.StartUtc)];

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
            RestingHeartRate = week.Where(d => d.RestingHeartRate.HasValue).MaxBy(d => d.Date)?.RestingHeartRate,
            RecentExercises = exercises,
            LatestExerciseLocalStart = exercises.Count > 0 ? profile.ToLocal(exercises[^1].StartUtc) : null,
            LocalToday = localNow.Date
        };
    }

    /// <param name="withExercise">False when the full <see cref="ActivityReport"/> follows anyway.</param>
    public string Describe(bool withExercise = true)
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
        string exercise = withExercise ? DescribeExercise() : string.Empty;
        if (exercise.Length > 0)
        {
            parts.Add(exercise);
        }
        return string.Join(Environment.NewLine, parts);
    }

    /// <summary>
    /// The watch's workouts of the week in one line, e.g. "Workouts on the watch (7 days): 3, 1h46m active, 114 Active
    /// Zone Minutes; latest today 16:35 Skipping rope, 30m20s, avg heart rate 152 bpm". Interval timer workouts are
    /// not in the profile; ActivityReport puts both together.
    /// </summary>
    internal string DescribeExercise()
    {
        if (RecentExercises.Count == 0 || LatestExerciseLocalStart is not DateTime start)
        {
            return string.Empty;
        }
        int active = RecentExercises.Sum(e => e.ActiveSeconds);
        int zoneMinutes = RecentExercises.Sum(e => e.ActiveZoneMinutes ?? 0);
        HealthExercise latest = RecentExercises[^1];
        int daysAgo = (LocalToday - start.Date).Days;
        string when = daysAgo switch { 0 => "today", 1 => "yesterday", _ => $"{daysAgo} days ago" };
        string text = $"Workouts on the watch (7 days): {RecentExercises.Count}, {ActivityReport.Duration(active)} active";
        if (zoneMinutes > 0)
        {
            text += $", {zoneMinutes} Active Zone Minutes";
        }
        text += $"; latest {when} {start:HH:mm} {(latest.Name.Length > 0 ? latest.Name : latest.Type)}, {ActivityReport.Duration(latest.ActiveSeconds)}";
        if (latest.AverageHeartRate is int heartRate)
        {
            text += $", avg heart rate {heartRate} bpm";
        }
        return text;
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
