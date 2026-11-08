using Akagi.Connectors.TimeTracker;
using System.Globalization;
using System.Text;

namespace Akagi.Life.Health;

/// <summary>
/// His workouts over the last days from both sources: the exercise sessions his watch recorded (Google Health, with
/// heart rate, zones and Active Zone Minutes) and the interval timer workouts from the TimeTracker (routine, rounds,
/// finished or stopped early). A timer workout that overlaps a watch session is the same workout and shown once.
/// </summary>
internal static class ActivityReport
{
    /// <summary>
    /// How far apart a timer workout and a watch session may start or end and still be the same workout.
    /// </summary>
    public static readonly TimeSpan MatchTolerance = TimeSpan.FromMinutes(10);

    private sealed record Item(DateTime Start, DateTime End, HealthExercise? Watch, TimeTrackerWorkouts.Session? Timer)
    {
        public int ActiveSeconds => Watch?.ActiveSeconds ?? Timer?.TotalSeconds ?? (int)(End - Start).TotalSeconds;
    }

    /// <param name="timer">The timer workouts, or null when they could not be read; <paramref name="timerNote"/> then says why.</param>
    public static string Describe(LifeProfile profile, IReadOnlyList<TimeTrackerWorkouts.Session>? timer, string? timerNote, DateTime nowUtc, int days)
    {
        DateTime today = profile.ToLocal(nowUtc).Date;
        DateTime from = today.AddDays(1 - days);
        List<Item> all = Merge(profile, timer ?? []);
        List<Item> recent = [.. all.Where(i => i.Start.Date >= from && i.Start.Date <= today)];
        string range = string.Create(CultureInfo.InvariantCulture, $"{from:yyyy-MM-dd} to {today:yyyy-MM-dd}");

        StringBuilder text = new();
        if (recent.Count == 0)
        {
            text.Append($"Workouts: none in the last {days} days ({range}), neither on the watch nor with the interval timer.");
            Item? last = all.LastOrDefault(i => i.Start.Date < from);
            if (last != null)
            {
                text.Append($" The last one was {Ago(last.Start, today)}: {Line(last)}");
            }
        }
        else
        {
            int dayCount = recent.Select(i => i.Start.Date).Distinct().Count();
            text.Append($"Workouts in the last {days} days ({range}): {Count(recent.Count, "workout")} on {Count(dayCount, "day")}, ");
            text.Append($"{Duration(recent.Sum(i => i.ActiveSeconds))} active");
            int zoneMinutes = recent.Sum(i => i.Watch?.ActiveZoneMinutes ?? 0);
            if (zoneMinutes > 0)
            {
                text.Append($", {zoneMinutes} Active Zone Minutes");
            }
            int stopped = recent.Count(i => i.Timer?.Completed == false);
            if (stopped > 0)
            {
                text.Append($", {stopped} stopped early");
            }
            text.Append($". Last one {Ago(recent[^1].Start, today)}.");
            foreach (Item item in recent)
            {
                text.AppendLine().Append("- ").Append(Line(item));
            }
        }
        if (timerNote != null)
        {
            text.AppendLine().Append("Interval timer: ").Append(timerNote);
        }
        return text.ToString();
    }

    /// <summary>
    /// Watch sessions and timer workouts in local time, oldest first, a timer workout joined to the watch session it
    /// overlaps most.
    /// </summary>
    private static List<Item> Merge(LifeProfile profile, IReadOnlyList<TimeTrackerWorkouts.Session> timer)
    {
        List<Item> items = [.. profile.HealthDays
            .SelectMany(d => d.Exercises)
            .Select(e => new Item(profile.ToLocal(e.StartUtc), profile.ToLocal(e.EndUtc), e, null))];

        foreach (TimeTrackerWorkouts.Session session in timer)
        {
            DateTime end = session.End ?? session.Start.AddSeconds(session.TotalSeconds ?? 0);
            int index = -1;
            TimeSpan best = TimeSpan.MinValue;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.Timer != null || item.Watch == null)
                {
                    continue;
                }
                TimeSpan overlap = (end < item.End ? end : item.End) - (session.Start > item.Start ? session.Start : item.Start);
                if (overlap > -MatchTolerance && overlap > best)
                {
                    best = overlap;
                    index = i;
                }
            }
            if (index >= 0)
            {
                items[index] = items[index] with { Timer = session };
            }
            else
            {
                items.Add(new Item(session.Start, end, null, session));
            }
        }
        items.Sort((a, b) => a.Start.CompareTo(b.Start));
        return items;
    }

    private static string Line(Item item)
    {
        StringBuilder line = new(item.Start.ToString("ddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (item.End > item.Start)
        {
            line.Append('-').Append(item.End.ToString(item.End.Date == item.Start.Date ? "HH:mm" : "ddd HH:mm", CultureInfo.InvariantCulture));
        }
        line.Append(' ');

        List<string> parts = [];
        if (item.Watch is HealthExercise watch)
        {
            line.Append(watch.Name.Length > 0 ? watch.Name : Humanize(watch.Type));
            line.Append(item.Timer != null
                ? $" (watch and interval timer \"{TimerName(item.Timer)}\")"
                : watch.AutoDetected ? " (watch, detected automatically)" : " (watch)");
            if (item.Timer != null)
            {
                parts.AddRange(TimerParts(item.Timer, withTotal: false));
            }
            parts.Add($"{Duration(watch.ActiveSeconds)} active");
            if (watch.DistanceMeters is double meters and > 0)
            {
                parts.Add(meters >= 1000 ? string.Create(CultureInfo.InvariantCulture, $"{meters / 1000:0.0} km") : $"{meters:0} m");
            }
            if (watch.Steps is int steps and > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{steps:N0} steps"));
            }
            if (watch.AverageHeartRate is int heartRate)
            {
                parts.Add($"avg heart rate {heartRate} bpm");
            }
            string zones = Zones(watch);
            if (zones.Length > 0)
            {
                parts.Add(zones);
            }
            if (watch.ActiveZoneMinutes is int zoneMinutes)
            {
                parts.Add($"{zoneMinutes} Active Zone Minutes");
            }
            if (watch.Calories is int calories)
            {
                parts.Add($"{calories} kcal");
            }
        }
        else if (item.Timer is TimeTrackerWorkouts.Session session)
        {
            line.Append(TimerName(session)).Append(" (interval timer, no watch session)");
            parts.AddRange(TimerParts(session, withTotal: true));
            if (parts.Count == 0)
            {
                parts.Add(session.Raw);
            }
        }
        line.Append(": ").Append(string.Join(", ", parts));
        return line.ToString();
    }

    private static string TimerName(TimeTrackerWorkouts.Session session) => session.Name.Length > 0 ? session.Name : "workout";

    private static IEnumerable<string> TimerParts(TimeTrackerWorkouts.Session session, bool withTotal)
    {
        if (session.Rounds is int rounds)
        {
            yield return session.PlannedRounds is int planned && planned > 0 ? $"{rounds}/{planned} rounds" : $"{rounds} rounds";
        }
        if (session.WorkSeconds is int work && work > 0)
        {
            yield return $"{Duration(work)} work";
        }
        if (session.RestSeconds is int rest && rest > 0)
        {
            yield return $"{Duration(rest)} rest";
        }
        if (withTotal && session.TotalSeconds is int total && total > 0)
        {
            yield return $"{Duration(total)} total";
        }
        if (session.Completed is bool completed)
        {
            yield return completed ? "finished" : "stopped early";
        }
    }

    private static string Zones(HealthExercise watch)
    {
        List<string> zones = [];
        void Add(string name, int? seconds)
        {
            if (seconds is int value && value >= 30)
            {
                zones.Add($"{name} {Duration(value)}");
            }
        }
        Add("light", watch.LightSeconds);
        Add("moderate", watch.ModerateSeconds);
        Add("vigorous", watch.VigorousSeconds);
        Add("peak", watch.PeakSeconds);
        return zones.Count == 0 ? string.Empty : "heart rate zones " + string.Join(" / ", zones);
    }

    /// <summary>
    /// JUMPING_ROPE as "Jumping rope".
    /// </summary>
    private static string Humanize(string type)
    {
        if (type.Length == 0)
        {
            return "Workout";
        }
        string words = type.Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static string Ago(DateTime start, DateTime today) => (today - start.Date).Days switch
    {
        0 => "today",
        1 => "yesterday",
        int daysAgo => $"{daysAgo} days ago"
    };

    internal static string Duration(int seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours}h{span.Minutes:00}m";
        }
        return span.Seconds == 0 ? $"{span.Minutes}m" : span.Minutes == 0 ? $"{span.Seconds}s" : $"{span.Minutes}m{span.Seconds:00}s";
    }
}
