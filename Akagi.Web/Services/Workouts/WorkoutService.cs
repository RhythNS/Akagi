using Akagi.Web.Data;
using Akagi.Web.Models.TimeTrackers;
using Akagi.Web.Models.Workouts;
using MongoDB.Driver;

namespace Akagi.Web.Services.Workouts;

public interface IWorkoutService
{
    public Task<List<WorkoutRoutine>> ListAsync(string userId);
    public Task<WorkoutRoutine?> GetAsync(string userId, string id);
    public Task<WorkoutRoutine> SaveAsync(WorkoutRoutine routine);
    public Task DeleteAsync(string userId, string id);
    /// <summary>The user's time trackers, to log workouts into.</summary>
    public Task<List<Definition>> TrackersAsync(string userId);
    /// <summary>Writes the result into the routine's tracker; returns the tracker, or null when it logs nowhere.</summary>
    public Task<Definition?> LogAsync(string userId, WorkoutRoutine routine, WorkoutResult result);
}

/// <summary>
/// Saved workouts and their results in the time tracker, in the website's database.
/// </summary>
public class WorkoutService : IWorkoutService
{
    private readonly IWorkoutDatabase _workouts;
    private readonly IDefinitionDatabase _definitions;
    private readonly IEntryDatabase _entries;

    public WorkoutService(IWorkoutDatabase workouts, IDefinitionDatabase definitions, IEntryDatabase entries)
    {
        _workouts = workouts;
        _definitions = definitions;
        _entries = entries;
    }

    public async Task<List<WorkoutRoutine>> ListAsync(string userId) =>
        [.. (await _workouts.GetDocumentsByPredicateAsync(Builders<WorkoutRoutine>.Filter.Eq(w => w.UserId, userId)))
            .OrderByDescending(w => w.LastRunAt ?? w.CreatedAt)];

    public async Task<WorkoutRoutine?> GetAsync(string userId, string id)
    {
        WorkoutRoutine? routine = await _workouts.GetDocumentByIdAsync(id);
        return routine?.UserId == userId ? routine : null;
    }

    public Task<WorkoutRoutine> SaveAsync(WorkoutRoutine routine) => _workouts.SaveDocumentAsync(routine);

    public async Task DeleteAsync(string userId, string id)
    {
        if (await GetAsync(userId, id) != null)
        {
            await _workouts.DeleteDocumentAsync(id);
        }
    }

    public Task<List<Definition>> TrackersAsync(string userId) =>
        _definitions.GetDocumentsByPredicateAsync(Builders<Definition>.Filter.Eq(d => d.UserId, userId));

    public async Task<Definition?> LogAsync(string userId, WorkoutRoutine routine, WorkoutResult result)
    {
        Definition? definition = routine.LogTo switch
        {
            WorkoutLogTarget.Nowhere => null,
            WorkoutLogTarget.Tracker when routine.TrackerDefinitionId is string id => await _definitions.GetDocumentByIdAsync(id) is { } found && found.UserId == userId ? found : await WorkoutsTrackerAsync(userId),
            _ => await WorkoutsTrackerAsync(userId)
        };
        if (definition == null)
        {
            return null;
        }
        await _entries.SaveDocumentAsync(WorkoutLog.Build(definition, result));
        return definition;
    }

    /// <summary>
    /// The user's "Workouts" tracker, created with the workout fields when it does not exist yet.
    /// </summary>
    private async Task<Definition> WorkoutsTrackerAsync(string userId)
    {
        List<Definition> mine = await TrackersAsync(userId);
        Definition? existing = mine.FirstOrDefault(d => string.Equals(d.Name, WorkoutLog.DefinitionName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }
        return await _definitions.SaveDocumentAsync(new Definition { Name = WorkoutLog.DefinitionName, UserId = userId, Fields = WorkoutLog.Fields() });
    }
}
