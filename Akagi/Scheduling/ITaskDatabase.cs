using Akagi.Data;
using Akagi.Scheduling.Tasks;

namespace Akagi.Scheduling;

internal interface ITaskDatabase : IDatabase<BaseTask>
{
    public Task<List<BaseTask>> GetTasks();
}
