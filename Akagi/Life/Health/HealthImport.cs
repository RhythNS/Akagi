using System.Globalization;
using System.Text.Json;

namespace Akagi.Life.Health;

/// <summary>
/// Imports health exports into a <see cref="LifeProfile"/>. Health Connect has no server API, so data reaches Akagi
/// as JSON produced on the phone (export apps, automation tools or a small companion script) and uploaded to the bot.
///
/// Accepted shape (keys are case insensitive, Health Connect record names work as aliases):
/// <code>
/// {
///   "sleep": [ { "start": "2026-10-26T23:10:00+01:00", "end": "2026-10-27T06:05:00+01:00" } ],
///   "steps": [ { "date": "2026-10-27", "count": 5234 }, { "start": "...", "end": "...", "count": 120 } ],
///   "restingHeartRate": [ { "date": "2026-10-27", "bpm": 58 } ]
/// }
/// </code>
/// Days contained in an import replace previously stored values, so re-importing the same export is harmless.
/// </summary>
internal static class HealthImport
{
    private static readonly string[] SleepKeys = ["sleep", "sleepSessions", "SleepSessionRecord", "SleepSession"];
    private static readonly string[] StepKeys = ["steps", "StepsRecord", "Steps"];
    private static readonly string[] HeartKeys = ["restingHeartRate", "RestingHeartRateRecord"];
    private static readonly string[] StartKeys = ["start", "startTime", "start_time", "from"];
    private static readonly string[] EndKeys = ["end", "endTime", "end_time", "to"];
    private static readonly string[] CountKeys = ["count", "steps", "value"];
    private static readonly string[] BpmKeys = ["bpm", "beatsPerMinute", "value"];

    internal record ImportResult(int SleepDays, int StepDays, int HeartRateDays, List<string> Warnings)
    {
        public int TotalDays => SleepDays + StepDays + HeartRateDays;

        public override string ToString()
        {
            string text = $"Imported sleep for {SleepDays} day(s), steps for {StepDays} day(s), resting heart rate for {HeartRateDays} day(s).";
            return Warnings.Count == 0 ? text : text + Environment.NewLine + string.Join(Environment.NewLine, Warnings.Take(10));
        }
    }

    public static ImportResult Import(LifeProfile profile, string json, string source = "import")
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Health import must be a JSON object.");
        }

        List<string> warnings = [];
        TimeZoneInfo zone = profile.TimeZone;

        Dictionary<string, List<(DateTime Start, DateTime End)>> sleepByDay = [];
        foreach (JsonElement session in EnumerateArray(root, SleepKeys))
        {
            DateTime? start = ReadTime(session, StartKeys);
            DateTime? end = ReadTime(session, EndKeys);
            if (start == null || end == null || end <= start)
            {
                warnings.Add($"Skipped sleep entry without valid start/end: {session.GetRawText()}");
                continue;
            }
            string day = LifeClock.DateKey(LifeClock.ToLocal(end.Value, zone));
            if (!sleepByDay.TryGetValue(day, out List<(DateTime, DateTime)>? sessions))
            {
                sessions = [];
                sleepByDay[day] = sessions;
            }
            sessions.Add((start.Value, end.Value));
        }

        Dictionary<string, int> stepsByDay = [];
        foreach (JsonElement record in EnumerateArray(root, StepKeys))
        {
            int? count = ReadInt(record, CountKeys);
            string? day = ReadDay(record, zone);
            if (count == null || day == null)
            {
                warnings.Add($"Skipped steps entry without date and count: {record.GetRawText()}");
                continue;
            }
            stepsByDay[day] = stepsByDay.GetValueOrDefault(day) + count.Value;
        }

        Dictionary<string, int> heartByDay = [];
        foreach (JsonElement record in EnumerateArray(root, HeartKeys))
        {
            int? bpm = ReadInt(record, BpmKeys);
            string? day = ReadDay(record, zone);
            if (bpm == null || day == null)
            {
                warnings.Add($"Skipped heart rate entry without date and bpm: {record.GetRawText()}");
                continue;
            }
            heartByDay[day] = bpm.Value;
        }

        foreach ((string day, List<(DateTime Start, DateTime End)> sessions) in sleepByDay)
        {
            HealthDay healthDay = profile.GetOrAddHealthDay(day);
            healthDay.SleepMinutes = (int)Math.Round(sessions.Sum(s => (s.End - s.Start).TotalMinutes));
            healthDay.SleepStartUtc = sessions.Min(s => s.Start);
            healthDay.SleepEndUtc = sessions.Max(s => s.End);
            healthDay.Source = source;
            healthDay.UpdatedAt = DateTime.UtcNow;
        }
        foreach ((string day, int steps) in stepsByDay)
        {
            HealthDay healthDay = profile.GetOrAddHealthDay(day);
            healthDay.Steps = steps;
            healthDay.Source = source;
            healthDay.UpdatedAt = DateTime.UtcNow;
        }
        foreach ((string day, int bpm) in heartByDay)
        {
            HealthDay healthDay = profile.GetOrAddHealthDay(day);
            healthDay.RestingHeartRate = bpm;
            healthDay.Source = source;
            healthDay.UpdatedAt = DateTime.UtcNow;
        }

        return new ImportResult(sleepByDay.Count, stepsByDay.Count, heartByDay.Count, warnings);
    }

    /// <summary>
    /// Records a manually reported night of sleep. The night is assigned to the local day of <paramref name="wokeUpUtc"/>.
    /// </summary>
    public static HealthDay LogSleep(LifeProfile profile, double hours, DateTime wokeUpUtc, string source)
    {
        HealthDay day = profile.GetOrAddHealthDay(profile.LocalDate(wokeUpUtc));
        day.SleepMinutes = (int)Math.Round(hours * 60);
        day.SleepEndUtc = wokeUpUtc;
        day.SleepStartUtc = wokeUpUtc.AddHours(-hours);
        day.Source = source;
        day.UpdatedAt = DateTime.UtcNow;
        return day;
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement root, string[] keys)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (keys.Any(k => string.Equals(k, property.Name, StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in property.Value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        yield return element;
                    }
                }
            }
        }
    }

    private static JsonElement? Find(JsonElement element, string[] keys)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (keys.Any(k => string.Equals(k, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value;
            }
        }
        return null;
    }

    private static DateTime? ReadTime(JsonElement element, string[] keys)
    {
        JsonElement? value = Find(element, keys);
        if (value is not { ValueKind: JsonValueKind.String } text)
        {
            return null;
        }
        return DateTimeOffset.TryParse(text.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.UtcDateTime
            : null;
    }

    private static int? ReadInt(JsonElement element, string[] keys)
    {
        JsonElement? value = Find(element, keys);
        if (value is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out double result))
        {
            return (int)Math.Round(result);
        }
        if (value is { ValueKind: JsonValueKind.String } text && double.TryParse(text.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return (int)Math.Round(parsed);
        }
        return null;
    }

    private static string? ReadDay(JsonElement element, TimeZoneInfo zone)
    {
        JsonElement? date = Find(element, ["date", "day"]);
        if (date is { ValueKind: JsonValueKind.String } text
            && DateTime.TryParseExact(text.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day))
        {
            return LifeClock.DateKey(day);
        }
        DateTime? start = ReadTime(element, StartKeys) ?? ReadTime(element, ["time", "timestamp"]);
        return start.HasValue ? LifeClock.DateKey(LifeClock.ToLocal(start.Value, zone)) : null;
    }
}
