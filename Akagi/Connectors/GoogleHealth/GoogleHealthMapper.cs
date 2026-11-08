using Akagi.Life;
using Akagi.Life.Health;
using System.Globalization;
using System.Text.Json;

namespace Akagi.Connectors.GoogleHealth;

/// <summary>
/// Maps Google Health API responses onto the health days of a <see cref="LifeProfile"/>. Sleep is filed under the
/// local date the user woke up, the convention <see cref="HealthDay"/> uses. Int64 values arrive as strings.
/// </summary>
internal static class GoogleHealthMapper
{
    public const string Source = "google-health";

    /// <summary>
    /// Pages of GET users/me/dataTypes/sleep/dataPoints:reconcile. The main sleep fills the night; other sessions
    /// count as naps.
    /// </summary>
    public static int ApplySleep(LifeProfile profile, IEnumerable<string> pages)
    {
        List<SleepSession> sessions = [];
        foreach (string page in pages)
        {
            using JsonDocument document = JsonDocument.Parse(page);
            foreach (JsonElement point in DataPoints(document.RootElement))
            {
                if (point.TryGetProperty("sleep", out JsonElement sleep) && SleepSession.Read(profile, sleep) is SleepSession session)
                {
                    sessions.Add(session);
                }
            }
        }

        int days = 0;
        foreach (IGrouping<string, SleepSession> day in sessions.GroupBy(s => s.WakeDate))
        {
            List<SleepSession> sleeps = [.. day];
            SleepSession main = sleeps.FirstOrDefault(s => s.IsMain)
                ?? sleeps.Where(s => !s.IsNap).MaxBy(s => s.MinutesAsleep ?? 0)
                ?? sleeps.MaxBy(s => s.MinutesAsleep ?? 0)!;

            HealthDay healthDay = profile.GetOrAddHealthDay(day.Key);
            healthDay.SleepMinutes = main.MinutesAsleep;
            healthDay.SleepEfficiency = main.Efficiency;
            healthDay.SleepStartUtc = main.StartUtc;
            healthDay.SleepEndUtc = main.EndUtc;
            // Stage sleep has DEEP/LIGHT/REM/AWAKE; classic logs only ASLEEP/RESTLESS/AWAKE
            healthDay.DeepSleepMinutes = main.Stage("DEEP");
            healthDay.RemSleepMinutes = main.Stage("REM");
            healthDay.LightSleepMinutes = main.Stage("LIGHT");
            healthDay.AwakeMinutes = main.Stage("AWAKE") ?? main.MinutesAwake;

            int naps = sleeps.Where(s => !ReferenceEquals(s, main)).Sum(s => s.MinutesAsleep ?? 0);
            healthDay.NapMinutes = naps > 0 ? naps : null;
            Touch(healthDay);
            days++;
        }
        return days;
    }

    public static int ApplySleep(LifeProfile profile, string json) => ApplySleep(profile, [json]);

    /// <summary>
    /// POST users/me/dataTypes/steps/dataPoints:dailyRollUp with windowSizeDays 1.
    /// </summary>
    public static int ApplySteps(LifeProfile profile, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("rollupDataPoints", out JsonElement points) || points.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        int days = 0;
        foreach (JsonElement point in points.EnumerateArray())
        {
            if (point.TryGetProperty("civilStartTime", out JsonElement start) && start.TryGetProperty("date", out JsonElement date)
                && DateKey(date) is string key
                && point.TryGetProperty("steps", out JsonElement steps) && Int(steps, "countSum") is int count)
            {
                HealthDay day = profile.GetOrAddHealthDay(key);
                day.Steps = count;
                Touch(day);
                days++;
            }
        }
        return days;
    }

    /// <summary>
    /// Pages of GET users/me/dataTypes/daily-resting-heart-rate/dataPoints.
    /// </summary>
    public static int ApplyRestingHeartRate(LifeProfile profile, IEnumerable<string> pages)
    {
        int days = 0;
        foreach (string page in pages)
        {
            using JsonDocument document = JsonDocument.Parse(page);
            foreach (JsonElement point in DataPoints(document.RootElement))
            {
                if (point.TryGetProperty("dailyRestingHeartRate", out JsonElement resting)
                    && resting.TryGetProperty("date", out JsonElement date) && DateKey(date) is string key
                    && Int(resting, "beatsPerMinute") is int bpm)
                {
                    HealthDay day = profile.GetOrAddHealthDay(key);
                    day.RestingHeartRate = bpm;
                    Touch(day);
                    days++;
                }
            }
        }
        return days;
    }

    public static int ApplyRestingHeartRate(LifeProfile profile, string json) => ApplyRestingHeartRate(profile, [json]);

    /// <summary>
    /// Pages of GET users/me/dataTypes/exercise/dataPoints for the local days <paramref name="first"/> to
    /// <paramref name="last"/>. Those days get exactly the sessions returned, so one deleted in the app goes away too.
    /// Returns the number of sessions.
    /// </summary>
    public static int ApplyExercise(LifeProfile profile, IEnumerable<string> pages, DateTime first, DateTime last)
    {
        List<(string Date, HealthExercise Exercise)> sessions = [];
        foreach (string page in pages)
        {
            using JsonDocument document = JsonDocument.Parse(page);
            foreach (JsonElement point in DataPoints(document.RootElement))
            {
                if (point.TryGetProperty("exercise", out JsonElement exercise) && ReadExercise(profile, point, exercise) is { } session)
                {
                    sessions.Add(session);
                }
            }
        }

        string from = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (HealthDay day in profile.HealthDays.Where(d => string.CompareOrdinal(d.Date, from) >= 0 && string.CompareOrdinal(d.Date, to) <= 0 && d.Exercises.Count > 0))
        {
            day.Exercises.Clear();
            Touch(day);
        }
        foreach ((string date, HealthExercise exercise) in sessions)
        {
            HealthDay day = profile.GetOrAddHealthDay(date);
            day.Exercises.RemoveAll(e => e.Id.Length > 0 && e.Id == exercise.Id);
            day.Exercises.Add(exercise);
            day.Exercises.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            Touch(day);
        }
        return sessions.Count;
    }

    public static int ApplyExercise(LifeProfile profile, string json, DateTime first, DateTime last) => ApplyExercise(profile, [json], first, last);

    private static (string Date, HealthExercise Exercise)? ReadExercise(LifeProfile profile, JsonElement point, JsonElement exercise)
    {
        if (!exercise.TryGetProperty("interval", out JsonElement interval) || Utc(interval, "startTime") is not DateTime start)
        {
            return null;
        }
        DateTime end = Utc(interval, "endTime") ?? start;
        string date = CivilDate(interval, "civilStartTime") ?? OffsetDate(start, interval, "startUtcOffset") ?? profile.LocalDate(start);

        JsonElement metrics = exercise.TryGetProperty("metricsSummary", out JsonElement m) ? m : default;
        JsonElement zones = metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("heartRateZoneDurations", out JsonElement z) ? z : default;
        int? Metric(string name) => metrics.ValueKind == JsonValueKind.Object ? Int(metrics, name) : null;
        int? Zone(string name) => zones.ValueKind == JsonValueKind.Object && zones.TryGetProperty(name, out JsonElement value) ? Seconds(value) : null;

        string recording = point.TryGetProperty("dataSource", out JsonElement source) && source.TryGetProperty("recordingMethod", out JsonElement method)
            ? method.GetString() ?? string.Empty
            : string.Empty;
        double? millimeters = metrics.ValueKind == JsonValueKind.Object && metrics.TryGetProperty("distanceMillimeters", out JsonElement distance)
            && distance.ValueKind == JsonValueKind.Number ? distance.GetDouble() : null;

        return (date, new HealthExercise
        {
            Id = point.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty,
            Type = exercise.TryGetProperty("exerciseType", out JsonElement type) ? type.GetString() ?? string.Empty : string.Empty,
            Name = exercise.TryGetProperty("displayName", out JsonElement display) ? display.GetString() ?? string.Empty : string.Empty,
            StartUtc = start,
            EndUtc = end,
            ActiveSeconds = exercise.TryGetProperty("activeDuration", out JsonElement active) && Seconds(active) is int seconds
                ? seconds
                : (int)Math.Round((end - start).TotalSeconds),
            Calories = Metric("caloriesKcal"),
            Steps = Metric("steps"),
            DistanceMeters = millimeters is double mm ? Math.Round(mm / 1000.0, 1) : null,
            AverageHeartRate = Metric("averageHeartRateBeatsPerMinute"),
            ActiveZoneMinutes = Metric("activeZoneMinutes"),
            LightSeconds = Zone("lightTime"),
            ModerateSeconds = Zone("moderateTime"),
            VigorousSeconds = Zone("vigorousTime"),
            PeakSeconds = Zone("peakTime"),
            AutoDetected = recording == "PASSIVELY_MEASURED"
        });
    }

    /// <summary>
    /// A Duration such as "1820s" or "1820.5s".
    /// </summary>
    private static int? Seconds(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 1 } text && text.EndsWith('s')
        && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            ? (int)Math.Round(seconds)
            : null;

    /// <summary>
    /// The nextPageToken of a list or reconcile response; null on the last page.
    /// </summary>
    public static string? NextPageToken(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("nextPageToken", out JsonElement token) && token.GetString() is { Length: > 0 } text ? text : null;
    }

    private sealed class SleepSession
    {
        public required string WakeDate { get; init; }
        public DateTime? StartUtc { get; init; }
        public DateTime? EndUtc { get; init; }
        public bool IsMain { get; init; }
        public bool IsNap { get; init; }
        public int? MinutesAsleep { get; init; }
        public int? MinutesAwake { get; init; }
        public int? Efficiency { get; init; }
        public Dictionary<string, int> Stages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int? Stage(string type) => Stages.TryGetValue(type, out int minutes) ? minutes : null;

        public static SleepSession? Read(LifeProfile profile, JsonElement sleep)
        {
            if (!sleep.TryGetProperty("interval", out JsonElement interval))
            {
                return null;
            }
            DateTime? start = Utc(interval, "startTime");
            DateTime? end = Utc(interval, "endTime");
            string? wakeDate = CivilDate(interval, "civilEndTime") ?? OffsetDate(end, interval, "endUtcOffset")
                ?? (end.HasValue ? profile.LocalDate(end.Value) : null);
            if (wakeDate == null)
            {
                return null;
            }

            JsonElement metadata = sleep.TryGetProperty("metadata", out JsonElement m) ? m : default;
            JsonElement summary = sleep.TryGetProperty("summary", out JsonElement s) ? s : default;
            int? asleep = summary.ValueKind == JsonValueKind.Object ? Int(summary, "minutesAsleep") : null;
            int? inPeriod = summary.ValueKind == JsonValueKind.Object ? Int(summary, "minutesInSleepPeriod") : null;
            if (inPeriod == null && start.HasValue && end.HasValue)
            {
                inPeriod = (int)Math.Round((end.Value - start.Value).TotalMinutes);
            }

            SleepSession session = new()
            {
                WakeDate = wakeDate,
                StartUtc = start,
                EndUtc = end,
                // The reference names the flag mainSleep, the reconcile sample shows main
                IsMain = Bool(metadata, "mainSleep") || Bool(metadata, "main"),
                IsNap = Bool(metadata, "nap"),
                MinutesAsleep = asleep,
                MinutesAwake = summary.ValueKind == JsonValueKind.Object ? Int(summary, "minutesAwake") : null,
                // Google's definition: minutes asleep over minutes in bed
                Efficiency = asleep.HasValue && inPeriod is > 0 ? (int)Math.Round(asleep.Value * 100.0 / inPeriod.Value) : null
            };
            if (summary.ValueKind == JsonValueKind.Object && summary.TryGetProperty("stagesSummary", out JsonElement stages) && stages.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement stage in stages.EnumerateArray())
                {
                    if (stage.TryGetProperty("type", out JsonElement type) && type.GetString() is { Length: > 0 } name && Int(stage, "minutes") is int minutes)
                    {
                        session.Stages[name] = minutes;
                    }
                }
            }
            return session;
        }
    }

    private static IEnumerable<JsonElement> DataPoints(JsonElement root) =>
        root.TryGetProperty("dataPoints", out JsonElement points) && points.ValueKind == JsonValueKind.Array ? points.EnumerateArray() : [];

    private static void Touch(HealthDay day)
    {
        day.Source = Source;
        day.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reads numbers and int64 values serialized as strings.
    /// </summary>
    private static int? Int(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return (int)number;
        }
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? (int)parsed
            : null;
    }

    private static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static DateTime? Utc(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

    /// <summary>
    /// {"date": {"year": 2026, "month": 2, "day": 26}} as "2026-02-26".
    /// </summary>
    private static string? CivilDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement civil) && civil.TryGetProperty("date", out JsonElement date) ? DateKey(date) : null;

    private static string? DateKey(JsonElement date)
    {
        if (Int(date, "year") is not int year || Int(date, "month") is not int month || Int(date, "day") is not int day || year <= 0)
        {
            return null;
        }
        return new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The local date of a UTC time using the offset Google recorded with it, such as "-14400s".
    /// </summary>
    private static string? OffsetDate(DateTime? utc, JsonElement interval, string offsetName)
    {
        if (utc == null || !interval.TryGetProperty(offsetName, out JsonElement offset) || offset.GetString() is not { Length: > 1 } text || !text.EndsWith('s')
            || !double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            return null;
        }
        return utc.Value.AddSeconds(seconds).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
