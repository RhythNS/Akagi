namespace Akagi.Scheduling.Tasks;

internal abstract class DailyTask : BaseTask
{
    private TimeSpan _timeOfDay = TimeSpan.Zero;
    private DateTime _lastExecution = DateTime.MinValue;
    private DateTime? _nextExecution = null;

    public override DateTime ExecuteAt => _nextExecution ?? DateTime.UtcNow.Date.Add(TimeOfDay);
    public override bool CanBeDeleted => false;
    public TimeSpan TimeOfDay
    {
        get => _timeOfDay;
        set
        {
            if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Time of day must be between 00:00:00 and 23:59:59.");
            }
            SetProperty(ref _timeOfDay, value);
        }
    }
    public DateTime LastExecution
    {
        get => _lastExecution;
        set => SetProperty(ref _lastExecution, value);
    }
    public DateTime? NextExecution
    {
        get => _nextExecution ??= DateTime.UtcNow.Date.Add(_timeOfDay);
        set => SetProperty(ref _nextExecution, value);
    }

    public override Task ExecuteAsync()
    {
        DateTime now = DateTime.UtcNow;
        if (_lastExecution.Date == now.Date)
        {
            SetNextExecution(now);
            throw new InvalidOperationException("Task has already been executed today.");
        }
        LastExecution = now;
        SetNextExecution(now);
        return ExecuteTaskAsync();
    }

    protected abstract Task ExecuteTaskAsync();

    private void SetNextExecution(DateTime now) => NextExecution = now.Date.AddDays(1).Add(_timeOfDay);
}
