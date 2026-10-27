using Akagi.Life;
using Akagi.Life.Health;
using System.Globalization;
using System.Text.Json;

namespace Akagi.Connectors.Fitbit;

/// <summary>
/// Maps Fitbit Web API responses onto the health days of a <see cref="LifeProfile"/>.
/// Fitbit files sleep under the date the user woke up, which is the same convention <see cref="HealthDay"/> uses.
/// </summary>
internal static class FitbitHealthMapper
{
    public const string Source = "fitbit";

    /// <summary>
    /// GET /1.2/user/-/sleep/date/{start}/{end}.json. The main sleep fills the night; other logs count as naps.
    /// </summary>
    public static int ApplySleep(LifeProfile profile, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("sleep", out JsonElement logs) || logs.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        HashSet<string> days = [];
        foreach (IGrouping<string, JsonElement> day in logs.EnumerateArray().GroupBy(l => l.GetProperty("dateOfSleep").GetString() ?? string.Empty))
        {
            if (day.Key.Length == 0)
            {
                continue;
            }

            List<JsonElement> sleeps = [.. day];
            JsonElement main = sleeps.FirstOrDefault(s => s.TryGetProperty("isMainSleep", out JsonElement isMain) && isMain.GetBoolean());
            if (main.ValueKind == JsonValueKind.Undefined)
            {
                main = sleeps.MaxBy(s => Int(s, "minutesAsleep") ?? 0);
            }

            HealthDay healthDay = profile.GetOrAddHealthDay(day.Key);
            healthDay.SleepMinutes = Int(main, "minutesAsleep");
            healthDay.SleepEfficiency = Int(main, "efficiency");
            healthDay.SleepStartUtc = LocalToUtc(profile, main, "startTime");
            healthDay.SleepEndUtc = LocalToUtc(profile, main, "endTime");

            if (main.TryGetProperty("levels", out JsonElement levels) && levels.TryGetProperty("summary", out JsonElement summary))
            {
                // Stage sleep has deep/light/rem/wake; short or classic logs only have asleep/restless/awake
                healthDay.DeepSleepMinutes = StageMinutes(summary, "deep");
                healthDay.RemSleepMinutes = StageMinutes(summary, "rem");
                healthDay.LightSleepMinutes = StageMinutes(summary, "light");
                healthDay.AwakeMinutes = StageMinutes(summary, "wake") ?? StageMinutes(summary, "awake");
            }
            else
            {
                healthDay.AwakeMinutes = Int(main, "minutesAwake");
            }

            int naps = sleeps.Where(s => !s.Equals(main)).Sum(s => Int(s, "minutesAsleep") ?? 0);
            healthDay.NapMinutes = naps > 0 ? naps : null;
            Touch(healthDay);
            days.Add(day.Key);
        }
        return days.Count;
    }

    /// <summary>
    /// GET /1/user/-/activities/steps/date/{start}/{end}.json
    /// </summary>
    public static int ApplySteps(LifeProfile profile, string json)
    {
        int days = 0;
        foreach ((string date, JsonElement value) in TimeSeries(json, "activities-steps"))
        {
            if (int.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps))
            {
                HealthDay day = profile.GetOrAddHealthDay(date);
                day.Steps = steps;
                Touch(day);
                days++;
            }
        }
        return days;
    }

    /// <summary>
    /// GET /1/user/-/activities/heart/date/{start}/{end}.json. Days without a resting rate are skipped.
    /// </summary>
    public static int ApplyRestingHeartRate(LifeProfile profile, string json)
    {
        int days = 0;
        foreach ((string date, JsonElement value) in TimeSeries(json, "activities-heart"))
        {
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("restingHeartRate", out JsonElement resting) && resting.TryGetInt32(out int bpm))
            {
                HealthDay day = profile.GetOrAddHealthDay(date);
                day.RestingHeartRate = bpm;
                Touch(day);
                days++;
            }
        }
        return days;
    }

    private static IEnumerable<(string Date, JsonElement Value)> TimeSeries(string json, string key)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(key, out JsonElement series) || series.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return [.. series.EnumerateArray()
            .Where(e => e.TryGetProperty("dateTime", out _) && e.TryGetProperty("value", out _))
            .Select(e => (e.GetProperty("dateTime").GetString() ?? string.Empty, e.GetProperty("value").Clone()))
            .Where(e => e.Item1.Length == 10)];
    }

    private static void Touch(HealthDay day)
    {
        day.Source = Source;
        day.UpdatedAt = DateTime.UtcNow;
    }

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int number) ? number : null;

    private static int? StageMinutes(JsonElement summary, string stage) =>
        summary.TryGetProperty(stage, out JsonElement entry) ? Int(entry, "minutes") : null;

    /// <summary>
    /// Fitbit times are local to the user's Fitbit profile without an offset; the life profile's time zone converts them.
    /// </summary>
    private static DateTime? LocalToUtc(LifeProfile profile, JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)
            || !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            return null;
        }
        return LifeClock.ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), profile.TimeZone);
    }
}
