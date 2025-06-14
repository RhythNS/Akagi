using Akagi.Scheduling.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Akagi.Scheduling;

internal class SchedulerService : BackgroundService
{
    private readonly ITaskDatabase _taskDatabase;
    private readonly ILogger<SchedulerService> _logger;

    public SchedulerService(ITaskDatabase taskDatabase, ILogger<SchedulerService> logger)
    {
        _taskDatabase = taskDatabase;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            List<BaseTask> tasks = await _taskDatabase.GetTasks();

            foreach (BaseTask task in tasks)
            {
                if (task.ExecuteAt > DateTime.UtcNow)
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Executing task {TaskId} of type {TaskType}", task.Id, task.GetType().Name);
                    await task.ExecuteAsync();
                    _logger.LogInformation("Executed task {TaskId} of type {TaskType}", task.Id, task.GetType().Name);

                    if (task.CanBeDeleted == true)
                    {
                        await _taskDatabase.DeleteDocumentByIdAsync(task.Id!);
                        continue;
                    }

                    if (task.Dirty == true)
                    {
                        await _taskDatabase.SaveAsync(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing task {TaskId} of type {TaskType}", task.Id, task.GetType().Name);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
