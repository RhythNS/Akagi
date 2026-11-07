namespace Akagi.Life.Work;

internal class WorkSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Project { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
    /// <summary>
    /// Last time the user took (or was reminded to take) a break during this session.
    /// </summary>
    public DateTime? LastBreakUtc { get; set; }
    public string? TimeTrackerEntryId { get; set; }

    public TimeSpan Duration(DateTime nowUtc) => (EndUtc ?? nowUtc) - StartUtc;
}

internal class WorkLog
{
    public WorkSession? Active { get; set; }
    public List<WorkSession> Sessions { get; set; } = [];

    public WorkSession Start(string project, string task, DateTime nowUtc)
    {
        Active = new WorkSession { Project = project.Trim(), Task = task.Trim(), StartUtc = nowUtc };
        return Active;
    }

    /// <summary>A session with no sign of the user for this long is closed on its own.</summary>
    public static readonly TimeSpan MaxIdle = TimeSpan.FromHours(12);

    /// <summary>
    /// Closes the running session when nothing has been heard from the user for <see cref="MaxIdle"/>: no message,
    /// no break and no start since. It ends at that last sign, so a forgotten StopWork does not count as a day of work.
    /// </summary>
    public WorkSession? CloseIfIdle(DateTime nowUtc, DateTime? lastUserMessageUtc)
    {
        if (Active == null)
        {
            return null;
        }
        DateTime lastSign = Active.StartUtc;
        foreach (DateTime? sign in new[] { Active.LastBreakUtc, lastUserMessageUtc })
        {
            if (sign.HasValue && sign.Value > lastSign && sign.Value <= nowUtc)
            {
                lastSign = sign.Value;
            }
        }
        if (nowUtc - lastSign < MaxIdle)
        {
            return null;
        }
        string note = string.IsNullOrWhiteSpace(Active.Notes)
            ? "Closed automatically: nothing heard from him for 12 hours."
            : Active.Notes + " (closed automatically: nothing heard from him for 12 hours)";
        return Stop(lastSign, note);
    }

    public WorkSession? Stop(DateTime nowUtc, string notes)
    {
        if (Active == null)
        {
            return null;
        }

        WorkSession finished = Active;
        finished.EndUtc = nowUtc;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            finished.Notes = notes.Trim();
        }
        Sessions.Add(finished);
        if (Sessions.Count > LifeProfile.MaxWorkSessions)
        {
            Sessions.RemoveRange(0, Sessions.Count - LifeProfile.MaxWorkSessions);
        }
        Active = null;
        return finished;
    }
}

internal class WorkSummary
{
    public WorkSession? Active { get; init; }
    public TimeSpan ActiveDuration { get; init; }
    public TimeSpan SinceBreak { get; init; }
    public TimeSpan Today { get; init; }
    public TimeSpan Week { get; init; }
    public IReadOnlyList<(string Project, TimeSpan Time)> TodayByProject { get; init; } = [];
    public IReadOnlyList<(string Project, TimeSpan Time)> WeekByProject { get; init; } = [];

    public bool HasData => Active != null || Week > TimeSpan.Zero;

    /// <summary>
    /// Sums work per local day and per project. Sessions that span midnight are split at local midnight.
    /// </summary>
    public static WorkSummary Create(LifeProfile profile, DateTime nowUtc)
    {
        DateTime localNow = profile.ToLocal(nowUtc);
        DateTime todayStartUtc = LifeClock.ToUtc(localNow.Date, profile.TimeZone);
        DateTime weekStartUtc = LifeClock.ToUtc(localNow.Date.AddDays(-6), profile.TimeZone);

        List<WorkSession> sessions = [.. profile.Work.Sessions];
        if (profile.Work.Active != null)
        {
            sessions.Add(profile.Work.Active);
        }

        Dictionary<string, TimeSpan> today = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TimeSpan> week = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkSession session in sessions)
        {
            DateTime end = session.EndUtc ?? nowUtc;
            Add(week, session.Project, Overlap(session.StartUtc, end, weekStartUtc, nowUtc));
            Add(today, session.Project, Overlap(session.StartUtc, end, todayStartUtc, nowUtc));
        }

        WorkSession? active = profile.Work.Active;
        return new WorkSummary
        {
            Active = active,
            ActiveDuration = active?.Duration(nowUtc) ?? TimeSpan.Zero,
            SinceBreak = active == null ? TimeSpan.Zero : nowUtc - (active.LastBreakUtc ?? active.StartUtc),
            Today = Sum(today),
            Week = Sum(week),
            TodayByProject = Ordered(today),
            WeekByProject = Ordered(week)
        };
    }

    public string Describe()
    {
        List<string> lines = [];
        if (Active != null)
        {
            string task = string.IsNullOrWhiteSpace(Active.Task) ? string.Empty : $" (task: {Active.Task})";
            lines.Add($"Currently working on {Active.Project}{task} for {LifeClock.FormatDuration(ActiveDuration)}, {LifeClock.FormatDuration(SinceBreak)} since the last break");
        }
        if (Today > TimeSpan.Zero)
        {
            lines.Add($"Worked today: {LifeClock.FormatDuration(Today)} ({string.Join(", ", TodayByProject.Select(p => $"{p.Project} {LifeClock.FormatDuration(p.Time)}"))})");
        }
        if (Week > TimeSpan.Zero)
        {
            lines.Add($"Last 7 days: {LifeClock.FormatDuration(Week)} ({string.Join(", ", WeekByProject.Take(5).Select(p => $"{p.Project} {LifeClock.FormatDuration(p.Time)}"))})");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd)
    {
        DateTime from = start > windowStart ? start : windowStart;
        DateTime to = end < windowEnd ? end : windowEnd;
        return to > from ? to - from : TimeSpan.Zero;
    }

    private static void Add(Dictionary<string, TimeSpan> totals, string project, TimeSpan time)
    {
        if (time <= TimeSpan.Zero)
        {
            return;
        }
        string key = string.IsNullOrWhiteSpace(project) ? "(no project)" : project;
        totals[key] = totals.GetValueOrDefault(key) + time;
    }

    private static TimeSpan Sum(Dictionary<string, TimeSpan> totals) => totals.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    private static List<(string, TimeSpan)> Ordered(Dictionary<string, TimeSpan> totals) =>
        [.. totals.OrderByDescending(kvp => kvp.Value).Select(kvp => (kvp.Key, kvp.Value))];
}
