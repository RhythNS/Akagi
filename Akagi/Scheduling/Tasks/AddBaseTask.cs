using Akagi.Flow;

namespace Akagi.Scheduling.Tasks;

internal class AddBaseTask : ISystemInitializer
{
    private readonly ITaskDatabase _database;

    public AddBaseTask(ITaskDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync()
    {
        CleanUpTask cleanUpTask = new()
        {
            Name = "Daily Cleanup",
            Description = "Performs daily cleanup tasks.",
            TimeOfDay = new TimeSpan(2, 0, 0)
        };

        List<BaseTask> tasks = await _database.GetDocumentsAsync();
        BaseTask? existingTask = tasks.FirstOrDefault(t => t is CleanUpTask);
        if (existingTask == null)
        {
            await _database.SaveDocumentAsync(cleanUpTask);
        }
        else
        {
            CleanUpTask existingCleanUpTask = (CleanUpTask)existingTask;

            existingCleanUpTask.Name = cleanUpTask.Name;
            existingCleanUpTask.Description = cleanUpTask.Description;
            existingCleanUpTask.TimeOfDay = cleanUpTask.TimeOfDay;
            await _database.SaveDocumentAsync(existingTask);
        }
    }
}
