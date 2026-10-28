using Akagi.Bridge.Attributes;
using Akagi.Life;
using Akagi.Life.Health;
using Akagi.Life.Mood;
using Akagi.Life.Study;
using Akagi.Life.Work;
using System.Globalization;

namespace Akagi.Characters.Routines;

[Flags]
internal enum Weekdays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Workdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    Every = Workdays | Weekend
}

/// <summary>
/// Fires once per local day inside a time window, e.g. a morning greeting.
/// </summary>
[NodeDocu("Reaches out once a day at a local time, e.g. a morning greeting or an evening wind-down.")]
internal class DailyRoutine : Routine
{
    private string _timeOfDay = "08:00";
    private int _windowMinutes = 120;
    private Weekdays _days = Weekdays.Every;

    public string TimeOfDay
    {
        get => _timeOfDay;
        set => SetProperty(ref _timeOfDay, value);
    }
    [NodeDocu("How long after TimeOfDay the routine may still fire, e.g. when the server was down.")]
    public int WindowMinutes
    {
        get => _windowMinutes;
        set => SetProperty(ref _windowMinutes, value);
    }
    public Weekdays Days
    {
        get => _days;
        set => SetProperty(ref _days, value);
    }

    protected override string DefaultInstruction =>
        "Greet the user for the day. Mention something relevant you know about their day or how they slept, and ask one caring question.";

    public override string DescribeSchedule(LifeProfile profile) => $"daily {TimeOfDay} ({DescribeDays(Days)})";

    private static readonly Weekdays[] SingleDays =
        [Weekdays.Monday, Weekdays.Tuesday, Weekdays.Wednesday, Weekdays.Thursday, Weekdays.Friday, Weekdays.Saturday, Weekdays.Sunday];

    internal static string DescribeDays(Weekdays days) => days switch
    {
        Weekdays.Every => "every day",
        Weekdays.Workdays => "workdays",
        Weekdays.Weekend => "weekends",
        Weekdays.None => "never",
        _ => string.Join(", ", SingleDays.Where(d => days.HasFlag(d)).Select(d => d.ToString()[..3]))
    };

    /// <summary>
    /// Parses "daily", "weekdays", "weekends" or a list such as "mon,wed,fri".
    /// </summary>
    internal static Weekdays? ParseDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string value = text.Trim().ToLowerInvariant();
        switch (value)
        {
            case "daily" or "every day" or "everyday" or "every":
                return Weekdays.Every;
            case "weekdays" or "workdays":
                return Weekdays.Workdays;
            case "weekends" or "weekend":
                return Weekdays.Weekend;
        }

        Weekdays result = Weekdays.None;
        foreach (string part in value.Split([',', ' ', ';', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            Weekdays day = SingleDays.FirstOrDefault(d => part.Length >= 2 && d.ToString().StartsWith(part, StringComparison.OrdinalIgnoreCase));
            if (day == Weekdays.None)
            {
                return null;
            }
            result |= day;
        }
        return result == Weekdays.None ? null : result;
    }

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        if (!LifeClock.TryParseTimeOfDay(TimeOfDay, out int start))
        {
            return RoutineDecision.No($"invalid TimeOfDay '{TimeOfDay}'");
        }
        if (!Days.HasFlag(ToFlag(state.LocalNow.DayOfWeek)))
        {
            return RoutineDecision.No("not scheduled today");
        }
        // The second check covers windows that cross midnight
        if (FiredToday(state) || (LastFiredUtc.HasValue && state.NowUtc - LastFiredUtc.Value < TimeSpan.FromMinutes(Math.Max(1, WindowMinutes))))
        {
            return RoutineDecision.No("already fired today");
        }
        int end = (start + Math.Max(1, WindowMinutes)) % (24 * 60);
        if (!LifeClock.IsInRange(state.LocalMinutes, start, end))
        {
            return RoutineDecision.No("outside time window");
        }
        return RoutineDecision.Yes($"daily at {TimeOfDay}");
    }

    internal static Weekdays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Weekdays.Monday,
        DayOfWeek.Tuesday => Weekdays.Tuesday,
        DayOfWeek.Wednesday => Weekdays.Wednesday,
        DayOfWeek.Thursday => Weekdays.Thursday,
        DayOfWeek.Friday => Weekdays.Friday,
        DayOfWeek.Saturday => Weekdays.Saturday,
        _ => Weekdays.Sunday
    };
}

/// <summary>
/// Checks in when the user has been silent for a while. Fires once per silence.
/// </summary>
[NodeDocu("Checks in after the user has been silent for a number of hours (once per silence).")]
internal class InactivityRoutine : Routine
{
    private int _afterHours = 24;

    public int AfterHours
    {
        get => _afterHours;
        set => SetProperty(ref _afterHours, value);
    }

    protected override string DefaultInstruction =>
        "The user has not written in a while. Reach out warmly without guilt-tripping; reference something from your last conversation.";

    public override string DescribeSchedule(LifeProfile profile) => $"after {AfterHours}h of silence";

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        if (!state.LastUserMessageUtc.HasValue)
        {
            return RoutineDecision.No("no conversation yet");
        }
        TimeSpan silence = state.NowUtc - state.LastUserMessageUtc.Value;
        if (silence < TimeSpan.FromHours(AfterHours))
        {
            return RoutineDecision.No("user wrote recently");
        }
        if (LastFiredUtc.HasValue && LastFiredUtc.Value >= state.LastUserMessageUtc.Value)
        {
            return RoutineDecision.No("already reached out during this silence");
        }
        return RoutineDecision.Yes($"silent for {LifeClock.FormatDuration(silence)}", $"The user last wrote {LifeClock.FormatAge(silence)}.");
    }
}

/// <summary>
/// Reminds the user to take a break during long work sessions.
/// </summary>
[NodeDocu("Reminds the user to take a break when a tracked work session runs long.")]
internal class BreakRoutine : Routine
{
    private int _workMinutes = 90;

    public BreakRoutine()
    {
        // Break reminders are useful exactly while the user is busy chatting about work
        SkipWhenUserActiveMinutes = 0;
    }

    public int WorkMinutes
    {
        get => _workMinutes;
        set => SetProperty(ref _workMinutes, value);
    }

    protected override string DefaultInstruction =>
        "The user has been working for a long stretch. Nudge them to take a short break (stretch, water, look away from the screen) in one or two sentences.";

    public override string DescribeSchedule(LifeProfile profile) => $"after {WorkMinutes} minutes of tracked work without a break";

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        WorkSession? active = state.Profile.Work.Active;
        if (active == null)
        {
            return RoutineDecision.No("no active work session");
        }

        DateTime since = Latest(active.StartUtc, active.LastBreakUtc, LastFiredUtc);
        TimeSpan stretch = state.NowUtc - since;
        if (stretch < TimeSpan.FromMinutes(WorkMinutes))
        {
            return RoutineDecision.No("not working long enough");
        }
        return RoutineDecision.Yes($"working for {LifeClock.FormatDuration(stretch)}",
            $"They are working on {active.Project}{(string.IsNullOrEmpty(active.Task) ? string.Empty : $" ({active.Task})")} for {LifeClock.FormatDuration(active.Duration(state.NowUtc))}, {LifeClock.FormatDuration(stretch)} without a break.");
    }

    private static DateTime Latest(DateTime start, params DateTime?[] others)
    {
        DateTime latest = start;
        foreach (DateTime? other in others)
        {
            if (other.HasValue && other.Value > latest)
            {
                latest = other.Value;
            }
        }
        return latest;
    }
}

/// <summary>
/// Follows up on worries and events whose follow-up time has come.
/// </summary>
[NodeDocu("Follows up on worries and upcoming events recorded with RecordMood once their follow-up time arrives.")]
internal class FollowUpRoutine : Routine
{
    protected override string DefaultInstruction =>
        "Follow up on what was on the user's mind. Ask how it went, be genuinely curious, and use ResolveConcern once you know the outcome.";

    public override string DescribeSchedule(LifeProfile profile) => "when a recorded worry or event is due for a follow-up";

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        List<Concern> due = Due(state);
        if (due.Count == 0)
        {
            return RoutineDecision.No("nothing to follow up on");
        }
        string details = string.Join(Environment.NewLine, due.Select(c =>
            $"[{c.Id}] {(c.Kind == MoodKind.Event ? "event" : "worry")} from {state.Profile.ToLocal(c.CreatedAt).ToString("ddd yyyy-MM-dd", CultureInfo.InvariantCulture)}: {c.Text}"));
        return RoutineDecision.Yes($"{due.Count} follow-up(s) due", details);
    }

    public override void OnFired(RoutineState state)
    {
        base.OnFired(state);
        foreach (Concern concern in Due(state))
        {
            concern.FollowedUpAt = state.NowUtc;
        }
        state.Profile.MarkChanged();
    }

    private static List<Concern> Due(RoutineState state) =>
        [.. state.Profile.Concerns.Where(c => c.IsDueForFollowUp(state.NowUtc)).OrderBy(c => c.FollowUpAt).Take(3)];
}

/// <summary>
/// Notices a short night once per day and offers support.
/// </summary>
[NodeDocu("Once a day, after the user is up, reacts supportively when last night's sleep was short.")]
internal class ShortSleepRoutine : Routine
{
    private double _minSleepHours = 6;
    private string _notBefore = "08:00";
    private bool _reactToRestless = true;

    public double MinSleepHours
    {
        get => _minSleepHours;
        set => SetProperty(ref _minSleepHours, value);
    }
    public string NotBefore
    {
        get => _notBefore;
        set => SetProperty(ref _notBefore, value);
    }
    [NodeDocu("Also react to a long but restless night (little deep sleep, low efficiency, long awake), as tracked by the user's watch through Google Health.")]
    public bool ReactToRestless
    {
        get => _reactToRestless;
        set => SetProperty(ref _reactToRestless, value);
    }

    protected override string DefaultInstruction =>
        "The user slept badly. Be supportive and light: acknowledge it, suggest going easy today, and offer to help them prioritize.";

    public override string DescribeSchedule(LifeProfile profile) => $"from {NotBefore} after a night under {MinSleepHours.ToString("0.#", CultureInfo.InvariantCulture)}h";

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        if (FiredToday(state))
        {
            return RoutineDecision.No("already fired today");
        }
        if (LifeClock.TryParseTimeOfDay(NotBefore, out int notBefore) && state.LocalMinutes < notBefore)
        {
            return RoutineDecision.No("too early");
        }

        HealthSummary health = HealthSummary.Create(state.Profile, state.NowUtc);
        if (!health.LastSleepIsLastNight || health.LastSleep?.SleepMinutes is not int minutes)
        {
            return RoutineDecision.No("no sleep data for last night");
        }
        if (minutes < MinSleepHours * 60)
        {
            return RoutineDecision.Yes($"slept {LifeClock.FormatDuration(TimeSpan.FromMinutes(minutes))}", health.Describe());
        }
        if (ReactToRestless && health.LastNightWasRestless)
        {
            return RoutineDecision.Yes("restless night", health.Describe());
        }
        return RoutineDecision.No("slept enough");
    }
}

/// <summary>
/// Evening nudge to keep the Japanese study streak alive.
/// </summary>
[NodeDocu("In the evening, nudges the user to study if they have a streak going but have not studied today.")]
internal class StudyNudgeRoutine : Routine
{
    private string _timeOfDay = "19:30";
    private int _minStreak = 1;

    public string TimeOfDay
    {
        get => _timeOfDay;
        set => SetProperty(ref _timeOfDay, value);
    }
    public int MinStreak
    {
        get => _minStreak;
        set => SetProperty(ref _minStreak, value);
    }

    protected override string DefaultInstruction =>
        "Playfully remind the user to keep their Japanese study streak alive. Offer a quick five minute review of due words.";

    public override string DescribeSchedule(LifeProfile profile) => $"evenings from {TimeOfDay} when not studied yet";

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        if (FiredToday(state))
        {
            return RoutineDecision.No("already fired today");
        }
        if (!LifeClock.TryParseTimeOfDay(TimeOfDay, out int start) || state.LocalMinutes < start)
        {
            return RoutineDecision.No("too early");
        }

        StudySummary study = StudySummary.Create(state.Profile, state.NowUtc);
        if (study.StudiedToday)
        {
            return RoutineDecision.No("already studied today");
        }
        if (study.Streak < MinStreak)
        {
            return RoutineDecision.No("no streak to protect");
        }
        return RoutineDecision.Yes($"streak of {study.Streak} at risk", study.Describe());
    }
}

/// <summary>
/// A single check-in at a fixed time, usually scheduled by the character itself
/// ("ask how the dentist went", "wake-up call before the exam").
/// </summary>
[NodeDocu("Reaches out once at a fixed time. Usually created by the character with the ScheduleCheckIn tool.")]
internal class OneTimeRoutine : Routine
{
    private DateTime? _dueUtc;
    private double _expireAfterHours = 12;

    public OneTimeRoutine()
    {
        // The time was chosen on purpose, so quiet hours do not apply; only avoid cutting into a live exchange
        QuietHoursStart = "00:00";
        QuietHoursEnd = "00:00";
        SkipWhenUserActiveMinutes = 3;
    }

    [NodeDocu("When to reach out (UTC).")]
    public DateTime? DueUtc
    {
        get => _dueUtc;
        set => SetProperty(ref _dueUtc, value);
    }
    [NodeDocu("Give up when the check-in could not be delivered for this many hours after it was due.")]
    public double ExpireAfterHours
    {
        get => _expireAfterHours;
        set => SetProperty(ref _expireAfterHours, value);
    }

    public bool IsDone => LastFiredUtc.HasValue;

    protected override bool PausedByActivities => false;

    protected override string DefaultInstruction =>
        "Check in with the user about the thing you planned to ask about.";

    public override string DescribeSchedule(LifeProfile profile)
    {
        if (DueUtc == null)
        {
            return "once (no time set)";
        }
        string when = profile.ToLocal(DueUtc.Value).ToString("ddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return IsDone ? $"once at {when} (done)" : $"once at {when}";
    }

    public bool IsExpired(DateTime nowUtc) => DueUtc.HasValue && !IsDone && nowUtc - DueUtc.Value > TimeSpan.FromHours(ExpireAfterHours);

    protected override RoutineDecision EvaluateCore(RoutineState state)
    {
        if (IsDone)
        {
            return RoutineDecision.No("already done");
        }
        if (DueUtc == null)
        {
            return RoutineDecision.No("no time set");
        }
        if (state.NowUtc < DueUtc.Value)
        {
            return RoutineDecision.No("not due yet");
        }
        if (IsExpired(state.NowUtc))
        {
            return RoutineDecision.No("missed");
        }
        string scheduled = CreatedAtUtc.HasValue
            ? $"You scheduled this check-in for yourself {LifeClock.FormatAge(state.NowUtc - CreatedAtUtc.Value)}."
            : string.Empty;
        return RoutineDecision.Yes("scheduled check-in", scheduled);
    }

    public override void OnFired(RoutineState state)
    {
        base.OnFired(state);
        Enabled = false;
    }
}
