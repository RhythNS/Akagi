using Akagi.Data;

namespace Akagi.Scheduling.Tasks;

internal abstract class BaseTask : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }
    public abstract DateTime ExecuteAt { get; }

    public abstract bool CanBeDeleted { get; }

    public abstract Task ExecuteAsync();
}
