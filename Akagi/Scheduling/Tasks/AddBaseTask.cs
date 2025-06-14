using Akagi.Utils;

namespace Akagi.Scheduling.Tasks;

internal class AddBaseTask : ISystemInitializer
{
    private readonly ITaskDatabase _database;

    public AddBaseTask(ITaskDatabase database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        CleanUpTask cleanUpTask = new()
        {
            Name = "Daily Cleanup",
            Description = "Performs daily cleanup tasks.",
            TimeOfDay = new TimeSpan(2, 0, 0)
        };

        BaseTask? existingTask = _database.GetDocumentsAsync().Result.FirstOrDefault(t => t is CleanUpTask);
        if (existingTask == null)
        {
            return _database.SaveDocumentAsync(cleanUpTask);
        }
        else
        {
            CleanUpTask existingCleanUpTask = (CleanUpTask)existingTask;

            existingCleanUpTask.Name = cleanUpTask.Name;
            existingCleanUpTask.Description = cleanUpTask.Description;
            existingCleanUpTask.TimeOfDay = cleanUpTask.TimeOfDay;
            return _database.SaveDocumentAsync(existingTask);
        }
    }
}
