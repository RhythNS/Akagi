using Akagi.Web.Data;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Akagi.Web.Models.Workouts;

/// <summary>
/// Where a finished workout is logged.
/// </summary>
public enum WorkoutLogTarget
{
    /// <summary>A time tracker called "Workouts", created with the workout fields the first time it is needed.</summary>
    Workouts,
    /// <summary>One of the user's own time trackers, <see cref="WorkoutRoutine.TrackerDefinitionId"/>.</summary>
    Tracker,
    Nowhere
}

/// <summary>
/// A saved interval workout: a start countdown, then work and rest in turn.
/// </summary>
[BsonIgnoreExtraElements]
public class WorkoutRoutine : Savable
{
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Give the workout a name.")]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Seconds of "get ready" before the first work; 0 starts with work.</summary>
    [Range(0, 3600, ErrorMessage = "The start time is 0 to 60 minutes.")]
    public int StartSeconds { get; set; } = 10;

    [Range(1, 24 * 3600, ErrorMessage = "A work lasts at least one second.")]
    public int WorkSeconds { get; set; } = 30;

    /// <summary>Seconds of rest after each work; 0 means no rests.</summary>
    [Range(0, 24 * 3600, ErrorMessage = "The rest time cannot be negative.")]
    public int RestSeconds { get; set; } = 10;

    /// <summary>How many works the workout has.</summary>
    [Range(1, 500, ErrorMessage = "A workout has 1 to 500 works.")]
    public int Rounds { get; set; } = 8;

    /// <summary>End right after the last work instead of resting once more.</summary>
    public bool SkipLastRest { get; set; } = true;

    public WorkoutLogTarget LogTo { get; set; } = WorkoutLogTarget.Workouts;

    /// <summary>The time tracker to log to when <see cref="LogTo"/> is <see cref="WorkoutLogTarget.Tracker"/>.</summary>
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string? TrackerDefinitionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastRunAt { get; set; }

    public WorkoutRoutine Copy() => (WorkoutRoutine)MemberwiseClone();
}
