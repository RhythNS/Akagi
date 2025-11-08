using Akagi.Characters;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers.SystemProcessors;
using Akagi.Users;

namespace Akagi.Receivers.Puppeteers;

internal abstract class Puppeteer : Savable
{
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    protected Context Context
    {
        get
        {
            if (_context == null)
            {
                throw new InvalidOperationException("Puppeteer has not been initialized with a context.");
            }
            return _context;
        }
    }
    protected ISystemProcessorDatabase SystemProcessorDatabase
    {
        get
        {
            if (_systemProcessorDatabase == null)
            {
                throw new InvalidOperationException("Puppeteer has not been initialized with a system processor database.");
            }
            return _systemProcessorDatabase;
        }
    }

    protected Character Character => Context.Character;
    protected Conversation Conversation => Context.Conversation;
    protected User User => Context.User;
    protected ICommunicator Communicator => Context.Communicator;
    protected ILLM LLM => Context.LLM;

    private Context? _context;
    private ISystemProcessorDatabase? _systemProcessorDatabase;

    public Task Init(Context context, ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase ?? throw new ArgumentNullException(nameof(systemProcessorDatabase), "System processor database cannot be null.");
        _context = context ?? throw new ArgumentNullException(nameof(context), "Context cannot be null.");

        return InnerInit();
    }

    public abstract Task ProcessAsync();

    protected virtual Task InnerInit() => Task.CompletedTask;
}
