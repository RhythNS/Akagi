using Akagi.Data;

namespace Akagi.Characters.TriggerPoints.Actions;

internal abstract class TriggerAction : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;

    private TriggerPoint.TriggerContext? _context;

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

    protected TriggerPoint.TriggerContext Context
    {
        get
        {
            if (_context == null)
            {
                throw new InvalidOperationException("TriggerAction has not been initialized with a context.");
            }
            return _context;
        }
    }

    public virtual Task Init(TriggerPoint.TriggerContext context)
    {
        _context = context;

        return Task.CompletedTask;
    }

    public abstract Task ExecuteAsync();
}
