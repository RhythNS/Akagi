using Akagi.Data;
using Akagi.Scheduling.Tasks;
using Microsoft.Extensions.Options;

namespace Akagi.Scheduling;

internal class TaskDatabase : Database<BaseTask>, ITaskDatabase
{
    public TaskDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "tasks")
    {
        options.OnChange(OnOptionsChange);
        OnOptionsChange(options.CurrentValue);
    }

    private void OnOptionsChange(DatabaseOptions options)
    {
    }

    public override bool CanSave(Savable savable) => savable is BaseTask;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((BaseTask)savable);

    public Task<List<BaseTask>> GetTasks() => GetDocumentsAsync();
}
