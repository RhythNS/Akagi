using Akagi.Life;
using System.Globalization;
using System.Text.Json;

namespace Akagi.Connectors.TimeTracker;

/// <summary>
/// Reads the workouts the interval timer in Akagi.Web logs to the TimeTracker ("Workouts": routine, date, start and
/// end, rounds done and planned, work, rest and total seconds, finished or aborted), so health reports can put them
/// next to the sessions his watch recorded (see <see cref="Life.Health.ActivityReport"/>).
/// </summary>
internal static class TimeTrackerWorkouts
{
    public const string DefinitionName = "Workouts";

    /// <summary>
    /// One logged workout, read from the entry's fields by name so a tracker of his own with similar fields works too.
    /// </summary>
    public sealed record Session(DateTime Start, DateTime? End, string Name, int? Rounds, int? PlannedRounds,
        int? WorkSeconds, int? RestSeconds, int? TotalSeconds, bool? Completed, string Raw);

    /// <summary>
    /// The tracker with that name (exact first, then containing it), or the "Workouts" one when no name is given.
    /// </summary>
    public static Definition? Find(IReadOnlyList<Definition> definitions, string? name)
    {
        string wanted = string.IsNullOrWhiteSpace(name) ? DefinitionName : name.Trim();
        return definitions.FirstOrDefault(d => string.Equals(d.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? definitions.FirstOrDefault(d => d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The timer workouts he logged, oldest first, read from the "Workouts" tracker or the one named. Null with the
    /// reason when there is no such tracker.
    /// </summary>
    public static async Task<(List<Session>? Sessions, string TrackerName, string? Problem)> LoadAsync(ITimeTrackerConnector connector,
        TimeTrackerUserConfig config, LifeProfile profile, string? trackerName)
    {
        List<Definition> definitions = await connector.GetDefinitionsAsync(config);
        Definition? definition = Find(definitions, trackerName);
        if (definition == null)
        {
            string others = definitions.Count == 0 ? "none" : string.Join(", ", definitions.Select(d => d.Name));
            string problem = string.IsNullOrWhiteSpace(trackerName)
                ? $"No \"{DefinitionName}\" tracker yet: he has not logged a workout with the interval timer. Trackers: {others}."
                : $"No tracker named \"{trackerName}\". Trackers: {others}.";
            return (null, trackerName ?? DefinitionName, problem);
        }

        List<Entry> entries = await connector.GetEntriesAsync(config, definition.Id);
        List<Session> sessions = [.. entries
            .Where(e => e.DefinitionId == definition.Id)
            .Select(e => Read(e, profile))
            .OrderBy(s => s.Start)];
        return (sessions, definition.Name, null);
    }

    private static string Key(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetter)]);

    private static readonly string[] NameKeys = ["workout", "name", "routine", "activity", "exercise", "title"];
    private static readonly string[] DateKeys = ["date", "day"];
    private static readonly string[] StartKeys = ["start", "starttime", "started", "from", "begin"];
    private static readonly string[] EndKeys = ["end", "endtime", "ended", "to", "until", "finish", "stop"];
    private static readonly string[] RoundKeys = ["rounds", "sets", "works", "intervals", "roundsdone", "completedrounds"];
    private static readonly string[] PlannedKeys = ["plannedrounds", "planned", "plannedsets", "targetrounds"];
    private static readonly string[] WorkKeys = ["workseconds", "work", "worktime"];
    private static readonly string[] RestKeys = ["restseconds", "rest", "resttime"];
    private static readonly string[] SecondsKeys = ["durationseconds", "seconds", "duration", "totalseconds"];
    private static readonly string[] MinuteKeys = ["minutes", "durationminutes", "totalminutes", "mins"];
    private static readonly string[] DoneKeys = ["completed", "complete", "done", "finished", "success"];

    /// <summary>
    /// The workout an entry holds. Date, Start and End are the local times the web app wrote; without them the time
    /// the entry was made is used.
    /// </summary>
    public static Session Read(Entry entry, LifeProfile profile)
    {
        Dictionary<string, object?> values = entry.Values.ToDictionary(kv => Key(kv.Key), kv => (object?)kv.Value);
        object? Get(string[] keys) => keys.Select(k => values.GetValueOrDefault(k)).FirstOrDefault(v => v != null && Text(v).Length > 0);

        DateTime made = profile.ToLocal(DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc));
        DateTime? startMoment = Moment(Get(StartKeys), profile);
        DateTime date = ParseDate(Get(DateKeys)) ?? startMoment?.Date ?? made.Date;
        DateTime start = startMoment ?? date + (ParseTime(Get(StartKeys)) ?? made.TimeOfDay);
        DateTime? end = Moment(Get(EndKeys), profile) ?? (ParseTime(Get(EndKeys)) is TimeSpan endTime ? date + endTime : null);
        if (end < start && end.Value.Date == start.Date)
        {
            end = end.Value.AddDays(1);
        }

        int? total = Whole(Get(SecondsKeys)) ?? (Number(Get(MinuteKeys)) is double minutes ? (int)Math.Round(minutes * 60) : null);
        string raw = string.Join(", ", entry.Values.Select(kv => $"{kv.Key}={Text(kv.Value)}"));
        return new Session(start, end, Text(Get(NameKeys)), Whole(Get(RoundKeys)), Whole(Get(PlannedKeys)),
            Whole(Get(WorkKeys)), Whole(Get(RestKeys)), total, Bool(Get(DoneKeys)), raw);
    }

    private static string Text(object? value) => value switch
    {
        null => string.Empty,
        JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => string.Empty,
        JsonElement json => json.GetRawText(),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static double? Number(object? value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Number } json)
        {
            return json.GetDouble();
        }
        return double.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;
    }

    private static int? Whole(object? value) => Number(value) is double number ? (int)Math.Round(number) : null;

    private static bool? Bool(object? value) => Text(value).ToLowerInvariant() switch
    {
        "true" or "yes" or "1" => true,
        "false" or "no" or "0" => false,
        _ => null
    };

    /// <summary>
    /// A full date and time (a DateTime field) in his local time; a UTC one is converted.
    /// </summary>
    private static DateTime? Moment(object? value, LifeProfile profile)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc ? profile.ToLocal(dateTime) : dateTime;
        }
        string text = Text(value);
        if (text.Length < 16 || text[4] != '-' || !text.Contains('T'))
        {
            return null;
        }
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
        {
            return null;
        }
        return parsed.Kind switch
        {
            DateTimeKind.Utc => profile.ToLocal(parsed),
            DateTimeKind.Local => profile.ToLocal(parsed.ToUniversalTime()),
            _ => parsed
        };
    }

    private static DateTime? ParseDate(object? value)
    {
        if (value is DateTime dateTime)
        {
            return dateTime.Date;
        }
        if (value is DateOnly dateOnly)
        {
            return dateOnly.ToDateTime(TimeOnly.MinValue);
        }
        string text = Text(value);
        return text.Length == 10 && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// A time of day (a Time field, "16:33:03.87").
    /// </summary>
    private static TimeSpan? ParseTime(object? value)
    {
        if (value is TimeOnly timeOnly)
        {
            return timeOnly.ToTimeSpan();
        }
        string text = Text(value);
        if (text.Length == 0 || text.Contains('-') || !text.Contains(':'))
        {
            return null;
        }
        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1)
            ? time
            : null;
    }
}
