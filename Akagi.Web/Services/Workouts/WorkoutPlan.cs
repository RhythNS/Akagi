using Akagi.Web.Models.Workouts;

namespace Akagi.Web.Services.Workouts;

public enum SegmentKind
{
    /// <summary>The countdown before the first work.</summary>
    Start,
    Work,
    Rest
}

/// <summary>
/// The sounds a workout plays, named after their files in <c>wwwroot/audio/workout</c>.
/// </summary>
public enum WorkoutCue
{
    GetReady,
    Go,
    RestTime,
    AlmostThere,
    GreatJob,
    /// <summary>The countdown in the last five seconds of every stage: 1.mp3 to 5.mp3.</summary>
    Count1,
    Count2,
    Count3,
    Count4,
    Count5
}

/// <summary>
/// One stretch of a workout. <see cref="Round"/> is the work unit it belongs to, counting from 1; the start countdown
/// is round 0.
/// </summary>
public sealed record Segment(SegmentKind Kind, int Round, double Start, double Duration)
{
    public double End => Start + Duration;
}

/// <summary>
/// Where a workout is after some seconds of it: the segment, how far into it, and what is done.
/// </summary>
public sealed record WorkoutPosition(Segment? Segment, int Index, double Elapsed, double SegmentRemaining, bool Finished,
    int CompletedRounds, double WorkSeconds, double RestSeconds)
{
    public Segment? Next { get; init; }
}

/// <summary>
/// A routine laid out in time: start countdown, then work and rest in turn, without the last rest when the routine
/// skips it. Positions and sounds are read from seconds of activity, so pausing is just not counting.
/// </summary>
public sealed class WorkoutPlan
{
    /// <summary>
    /// "Almost there" plays when this many seconds of a work are left.
    /// </summary>
    public const double AlmostThereSeconds = 10;

    public WorkoutPlan(WorkoutRoutine routine)
    {
        List<Segment> segments = [];
        double at = 0;
        if (routine.StartSeconds > 0)
        {
            segments.Add(new Segment(SegmentKind.Start, 0, at, routine.StartSeconds));
            at += routine.StartSeconds;
        }
        int rounds = Math.Max(1, routine.Rounds);
        for (int round = 1; round <= rounds; round++)
        {
            segments.Add(new Segment(SegmentKind.Work, round, at, Math.Max(1, routine.WorkSeconds)));
            at += Math.Max(1, routine.WorkSeconds);
            bool last = round == rounds;
            if (routine.RestSeconds > 0 && !(last && routine.SkipLastRest))
            {
                segments.Add(new Segment(SegmentKind.Rest, round, at, routine.RestSeconds));
                at += routine.RestSeconds;
            }
        }
        Segments = segments;
        Rounds = rounds;
        Total = at;
    }

    public IReadOnlyList<Segment> Segments { get; }

    public int Rounds { get; }

    /// <summary>
    /// Seconds from the start countdown to the end of the last segment.
    /// </summary>
    public double Total { get; }

    public double TotalWork => Segments.Where(s => s.Kind == SegmentKind.Work).Sum(s => s.Duration);

    public double TotalRest => Segments.Where(s => s.Kind == SegmentKind.Rest).Sum(s => s.Duration);

    public WorkoutPosition At(double elapsed)
    {
        elapsed = Math.Max(0, elapsed);
        double work = 0;
        double rest = 0;
        int completed = 0;
        for (int i = 0; i < Segments.Count; i++)
        {
            Segment segment = Segments[i];
            double done = Math.Clamp(elapsed - segment.Start, 0, segment.Duration);
            if (segment.Kind == SegmentKind.Work)
            {
                work += done;
                if (done >= segment.Duration)
                {
                    completed++;
                }
            }
            else if (segment.Kind == SegmentKind.Rest)
            {
                rest += done;
            }
        }
        if (elapsed >= Total)
        {
            return new WorkoutPosition(null, Segments.Count, Total, 0, true, completed, work, rest);
        }
        int index = 0;
        while (index < Segments.Count - 1 && elapsed >= Segments[index].End)
        {
            index++;
        }
        Segment current = Segments[index];
        return new WorkoutPosition(current, index, elapsed, current.End - elapsed, false, completed, work, rest)
        {
            Next = index + 1 < Segments.Count ? Segments[index + 1] : null
        };
    }

    /// <summary>
    /// Every sound with the second it plays at: get ready as the countdown begins, go as each work begins, almost
    /// there ten seconds before a work ends (when the work is longer than that), rest time as each rest begins, great
    /// job at the end. Rests of <see cref="RestGetReadyMinimum"/> seconds or more say get ready ten seconds before
    /// they end, and every stage counts down 5, 4, 3, 2, 1 in its last five seconds (a short stage only from where it
    /// begins, so its first sound is not drowned out).
    /// </summary>
    public IEnumerable<(double At, WorkoutCue Cue)> Cues()
    {
        List<(double At, WorkoutCue Cue)> cues = [];
        foreach (Segment segment in Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.Start:
                    cues.Add((segment.Start, WorkoutCue.GetReady));
                    break;
                case SegmentKind.Work:
                    cues.Add((segment.Start, WorkoutCue.Go));
                    if (segment.Duration > AlmostThereSeconds)
                    {
                        cues.Add((segment.End - AlmostThereSeconds, WorkoutCue.AlmostThere));
                    }
                    break;
                case SegmentKind.Rest:
                    cues.Add((segment.Start, WorkoutCue.RestTime));
                    if (segment.Duration >= RestGetReadyMinimum)
                    {
                        cues.Add((segment.End - RestGetReadySeconds, WorkoutCue.GetReady));
                    }
                    break;
            }
            for (int left = CountdownSeconds; left >= 1; left--)
            {
                double at = segment.End - left;
                if (at > segment.Start)
                {
                    cues.Add((at, Countdown(left)));
                }
            }
        }
        cues.Add((Total, WorkoutCue.GreatJob));
        return cues.OrderBy(c => c.At);
    }

    /// <summary>
    /// The last seconds of every stage are counted down.
    /// </summary>
    public const int CountdownSeconds = 5;

    /// <summary>
    /// A rest says get ready this many seconds before it ends...
    /// </summary>
    public const double RestGetReadySeconds = 10;

    /// <summary>
    /// ...when it lasts at least this long.
    /// </summary>
    public const double RestGetReadyMinimum = 15;

    public static WorkoutCue Countdown(int secondsLeft) => secondsLeft switch
    {
        1 => WorkoutCue.Count1,
        2 => WorkoutCue.Count2,
        3 => WorkoutCue.Count3,
        4 => WorkoutCue.Count4,
        _ => WorkoutCue.Count5
    };

    /// <summary>
    /// The sounds due after <paramref name="from"/> up to and including <paramref name="to"/>; with a negative
    /// <paramref name="from"/> the sounds at second 0 are included. Only the latest of each moment is returned when
    /// several were skipped at once, so a long gap does not play a pile of sounds.
    /// </summary>
    public List<WorkoutCue> CuesBetween(double from, double to)
    {
        List<(double At, WorkoutCue Cue)> due = [.. Cues().Where(c => c.At > from && c.At <= to)];
        if (due.Count <= 1)
        {
            return [.. due.Select(c => c.Cue)];
        }
        double latest = due.Max(c => c.At);
        return [.. due.Where(c => c.At == latest).Select(c => c.Cue)];
    }

    /// <summary>
    /// Reads a duration typed as seconds (90), minutes and seconds (1:30) or hours too (1:00:00); null when it is not one.
    /// </summary>
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string[] parts = text.Trim().Split(':');
        if (parts.Length > 3)
        {
            return null;
        }
        int total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value)
                || (i > 0 && value >= 60))
            {
                return null;
            }
            total = total * 60 + value;
        }
        return total;
    }

    public static string Format(double seconds)
    {
        int whole = (int)Math.Ceiling(Math.Max(0, seconds) - 1e-9);
        return whole >= 3600
            ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}"
            : $"{whole / 60}:{whole % 60:00}";
    }
}
