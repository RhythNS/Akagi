using Akagi.Characters;
using Akagi.Characters.Conversations;
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

    protected virtual Task InnerInit() => Task.CompletedTask;

    protected async Task<SystemProcessor> GetSingle(string id)
    {
        if (_systemProcessorDatabase == null)
        {
            throw new InvalidOperationException("System processor database has not been initialized.");
        }
        SystemProcessor? systemProcessor = await _systemProcessorDatabase.GetSystemProcessor(id);
        if (systemProcessor == null)
        {
            throw new InvalidOperationException($"System processor with id {id} not found.");
        }
        return systemProcessor;
    }

    protected async Task<SystemProcessor[]> GetMultiple(string[] ids)
    {
        if (_systemProcessorDatabase == null)
        {
            throw new InvalidOperationException("System processor database has not been initialized.");
        }

        SystemProcessor[] systemProcessors = await _systemProcessorDatabase.GetSystemProcessor(ids);
        return systemProcessors;
    }

    public abstract Task ProcessAsync();

    public virtual Message.Type CurrentMessageVisibility => Message.Type.System | Message.Type.Character | Message.Type.User;
}
