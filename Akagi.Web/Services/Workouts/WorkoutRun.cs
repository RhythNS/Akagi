using Akagi.Web.Models.Workouts;
using System.Text.Json;

namespace Akagi.Web.Services.Workouts;

/// <summary>
/// A workout under way, kept in the browser so a refresh comes back to it. Time is counted from the wall clock minus
/// pauses; the time the page was gone counts as paused, so the workout continues where it was left.
/// </summary>
public sealed class WorkoutRun
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// Away longer than this (closed tab, another page), the workout comes back paused instead of running on.
    /// </summary>
    public static readonly TimeSpan ResumeRunningWithin = TimeSpan.FromSeconds(20);

    public int Version { get; set; } = CurrentVersion;
    public string UserId { get; set; } = string.Empty;
    /// <summary>The routine as it was when started, so editing it meanwhile changes nothing.</summary>
    public WorkoutRoutine Routine { get; set; } = new();
    public DateTime StartedAtUtc { get; set; }
    public double PausedSeconds { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    /// <summary>The page last confirmed it was open; refreshed about every second.</summary>
    public DateTime LastSeenUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    /// <summary>Sounds were played up to this second of activity.</summary>
    public double HeardUntil { get; set; } = -1;
    /// <summary>The result went to the time tracker (or the user chose not to); never logged twice.</summary>
    public bool Logged { get; set; }
    /// <summary>The tracker it went to, for the summary.</summary>
    public string? LoggedTo { get; set; }
    /// <summary>It ended by abort.</summary>
    public bool Aborted { get; set; }
    /// <summary>What goes into the time tracker, chosen when it ended; kept so a failed save can be tried again.</summary>
    public AbortSave Save { get; set; } = AbortSave.WhatWasDone;
    /// <summary>The tracker it went to, to link from the summary.</summary>
    public string? LoggedDefinitionId { get; set; }

    public bool Paused => PausedAtUtc != null && FinishedAtUtc == null;
    public bool Finished => FinishedAtUtc != null;

    public static WorkoutRun Start(string userId, WorkoutRoutine routine, DateTime now) => new()
    {
        UserId = userId,
        Routine = routine.Copy(),
        StartedAtUtc = now,
        LastSeenUtc = now
    };

    /// <summary>
    /// Seconds of activity: wall clock since the start, without pauses, frozen while paused and once finished.
    /// </summary>
    public double Elapsed(DateTime now)
    {
        DateTime until = FinishedAtUtc ?? PausedAtUtc ?? now;
        return Math.Max(0, (until - StartedAtUtc).TotalSeconds - PausedSeconds);
    }

    public void Pause(DateTime now)
    {
        if (!Paused && !Finished)
        {
            PausedAtUtc = now;
        }
    }

    public void Resume(DateTime now)
    {
        if (PausedAtUtc is DateTime paused && !Finished)
        {
            PausedSeconds += Math.Max(0, (now - paused).TotalSeconds);
            PausedAtUtc = null;
        }
    }

    /// <summary>
    /// Ends the run at <paramref name="elapsed"/> seconds of activity, keeping that as its time.
    /// </summary>
    public void Finish(DateTime now, double elapsed, bool aborted)
    {
        if (Finished)
        {
            return;
        }
        Resume(now);
        // Pin the end so Elapsed stays at what was done
        PausedSeconds = Math.Max(0, (now - StartedAtUtc).TotalSeconds - elapsed);
        FinishedAtUtc = now;
        Aborted = aborted;
    }

    /// <summary>
    /// Coming back after a refresh or another page: the time away counts as paused. A short absence (a refresh) keeps
    /// it running; a long one leaves it paused so the user picks it up themselves.
    /// </summary>
    public void Restore(DateTime now)
    {
        if (Finished || Paused)
        {
            LastSeenUtc = now;
            return;
        }
        TimeSpan away = now - LastSeenUtc;
        if (away > TimeSpan.Zero)
        {
            PausedSeconds += away.TotalSeconds;
        }
        if (away > ResumeRunningWithin)
        {
            PausedAtUtc = now;
        }
        LastSeenUtc = now;
    }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = false };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static WorkoutRun? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            WorkoutRun? run = JsonSerializer.Deserialize<WorkoutRun>(json, Json);
            return run?.Version == CurrentVersion ? run : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
