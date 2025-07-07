using Akagi.Scheduling.Tasks;

namespace Akagi.Scheduling;

internal abstract class OneShotTask : BaseTask
{
    private DateTime _time = DateTime.MinValue;
    private bool _finished = false;

    public DateTime Time
    {
        get => _time;
        set => SetProperty(ref _time, value);
    }
    public bool Finished
    {
        get => _finished;
        set => SetProperty(ref _finished, value);
    }

    public override DateTime ExecuteAt => _time;

    public override bool CanBeDeleted => Finished;

    public override Task ExecuteAsync()
    {
        if (Finished)
        {
            throw new InvalidOperationException("Task has already been executed.");
        }
        Finished = true;
        return ExecuteTaskAsync();
    }

    protected abstract Task ExecuteTaskAsync();
}
