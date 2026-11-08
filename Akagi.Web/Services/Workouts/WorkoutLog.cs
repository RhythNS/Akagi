using Akagi.Web.Models.TimeTrackers;

namespace Akagi.Web.Services.Workouts;

/// <summary>
/// How an aborted workout goes into the time tracker.
/// </summary>
public enum AbortSave
{
    Nothing,
    /// <summary>Only what was done: the works finished, the seconds worked and rested.</summary>
    WhatWasDone,
    /// <summary>As if every work had been finished.</summary>
    AsCompleted
}

/// <summary>
/// What a workout amounted to, ready for the time tracker. Times are local.
/// </summary>
public sealed record WorkoutResult(string Name, DateTime Start, DateTime End, int Rounds, int PlannedRounds,
    int WorkSeconds, int RestSeconds, int ActiveSeconds, bool Completed)
{
    /// <summary>
    /// The result of a finished run: what was done, or with <paramref name="asCompleted"/> the whole routine.
    /// </summary>
    public static WorkoutResult From(WorkoutRun run, WorkoutPlan plan, bool asCompleted)
    {
        DateTime start = run.StartedAtUtc.ToLocalTime();
        DateTime endLocal = (run.FinishedAtUtc ?? DateTime.UtcNow).ToLocalTime();
        if (asCompleted)
        {
            return new WorkoutResult(run.Routine.Name, start, endLocal, plan.Rounds, plan.Rounds, (int)plan.TotalWork,
                (int)plan.TotalRest, (int)Math.Round(plan.Total), true);
        }
        WorkoutPosition position = plan.At(run.Elapsed(run.FinishedAtUtc ?? DateTime.UtcNow));
        return new WorkoutResult(run.Routine.Name, start, endLocal, position.CompletedRounds, plan.Rounds,
            (int)Math.Round(position.WorkSeconds), (int)Math.Round(position.RestSeconds), (int)Math.Round(position.Elapsed),
            position.Finished);
    }
}

/// <summary>
/// Turns a workout into a time tracker entry. The "Workouts" tracker gets its own fields; any other tracker gets
/// whatever of its fields a workout can fill, found by name, and the usual defaults for the rest.
/// </summary>
public static class WorkoutLog
{
    public const string DefinitionName = "Workouts";

    public static List<FieldDefinition> Fields() =>
    [
        new() { Name = "Workout", Type = FieldType.Text },
        new() { Name = "Date", Type = FieldType.Date },
        new() { Name = "Start", Type = FieldType.Time },
        new() { Name = "End", Type = FieldType.Time },
        new() { Name = "Rounds", Type = FieldType.Int },
        new() { Name = "Planned rounds", Type = FieldType.Int },
        new() { Name = "Work seconds", Type = FieldType.Int },
        new() { Name = "Rest seconds", Type = FieldType.Int },
        new() { Name = "Duration seconds", Type = FieldType.Int },
        new() { Name = "Completed", Type = FieldType.Boolean }
    ];

    private static string Key(string name) => new([.. name.ToLowerInvariant().Where(char.IsLetter)]);

    private static readonly string[] NameKeys = ["workout", "name", "routine", "activity", "exercise", "title", "description"];
    private static readonly string[] DateKeys = ["date", "day"];
    private static readonly string[] StartKeys = ["start", "starttime", "started", "from", "begin", "datetime", "time"];
    private static readonly string[] EndKeys = ["end", "endtime", "ended", "to", "until", "finish", "finished", "stop"];
    private static readonly string[] RoundKeys = ["rounds", "sets", "works", "intervals", "reps", "roundsdone", "completedrounds"];
    private static readonly string[] PlannedKeys = ["plannedrounds", "planned", "plannedsets", "targetrounds"];
    private static readonly string[] WorkKeys = ["workseconds", "work", "worktime"];
    private static readonly string[] RestKeys = ["restseconds", "rest", "resttime"];
    private static readonly string[] SecondsKeys = ["durationseconds", "seconds", "duration", "totalseconds"];
    private static readonly string[] MinuteKeys = ["minutes", "durationminutes", "totalminutes", "mins"];
    private static readonly string[] HourKeys = ["hours", "durationhours"];
    private static readonly string[] DoneKeys = ["completed", "complete", "done", "finished", "success"];

    public static Entry Build(Definition definition, WorkoutResult result)
    {
        Entry entry = new() { DefinitionId = definition.Id ?? string.Empty };
        foreach (FieldDefinition field in definition.Fields)
        {
            entry.Values[field.Name] = Value(field, result) ?? Default(field.Type);
        }
        return entry;
    }

    private static object? Value(FieldDefinition field, WorkoutResult result)
    {
        string key = Key(field.Name);
        bool Is(string[] keys) => keys.Contains(key);
        switch (field.Type)
        {
            case FieldType.Text:
                return Is(NameKeys) ? result.Name : null;
            case FieldType.Date:
                return Is(DateKeys) || Is(StartKeys) ? DateOnly.FromDateTime(result.Start) : Is(EndKeys) ? DateOnly.FromDateTime(result.End) : null;
            case FieldType.Time:
                return Is(EndKeys) ? TimeOnly.FromDateTime(result.End) : Is(StartKeys) ? TimeOnly.FromDateTime(result.Start) : null;
            case FieldType.DateTime:
                return Is(EndKeys) ? result.End : Is(StartKeys) || Is(DateKeys) ? result.Start : null;
            case FieldType.Boolean:
                return Is(DoneKeys) ? result.Completed : null;
            case FieldType.Int:
            case FieldType.Float:
                double? number = Is(PlannedKeys) ? result.PlannedRounds
                    : Is(RoundKeys) ? result.Rounds
                    : Is(WorkKeys) ? result.WorkSeconds
                    : Is(RestKeys) ? result.RestSeconds
                    : Is(SecondsKeys) ? result.ActiveSeconds
                    : Is(MinuteKeys) ? result.ActiveSeconds / 60.0
                    : Is(HourKeys) ? result.ActiveSeconds / 3600.0
                    : null;
                if (number is not double value)
                {
                    return null;
                }
                // Boxed one by one: a ternary would turn the int into a float
                if (field.Type == FieldType.Int)
                {
                    return (int)Math.Round(value);
                }
                return (float)Math.Round(value, 2);
            default:
                return null;
        }
    }

    /// <summary>
    /// What a new entry starts with on the entry page.
    /// </summary>
    public static object Default(FieldType type) => type switch
    {
        FieldType.Boolean => false,
        FieldType.Int => 0,
        FieldType.Float => 0.0f,
        FieldType.Text => string.Empty,
        FieldType.DateTime => DateTime.Today,
        FieldType.Date => DateOnly.FromDateTime(DateTime.Today),
        FieldType.Time => TimeOnly.FromDateTime(DateTime.Now),
        _ => string.Empty
    };
}
