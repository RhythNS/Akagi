using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Users;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.CharacterBehaviors;

internal abstract class CharacterBehavior : Savable
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
                throw new InvalidOperationException("CharacterBehavior has not been initialized with a context.");
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
                throw new InvalidOperationException("CharacterBehavior has not been initialized with a system processor database.");
            }
            return _systemProcessorDatabase;
        }
    }

    protected ILogger Logger
    {
        get
        {
            if (_logger == null)
            {
                throw new InvalidOperationException("CharacterBehavior has not been initialized with a logger.");
            }
            return _logger;
        }
    }

    protected Character Character => Context.Character;
    protected Conversation Conversation => Context.Conversation;
    protected User User => Context.User;
    protected ICommunicator Communicator => Context.Communicator;
    protected ILLMFactory LLMFactory => Context.LLMFactory;

    private Context? _context;
    private ISystemProcessorDatabase? _systemProcessorDatabase;
    private ILogger? _logger;

    public Task Init(ILogger logger, Context context, ISystemProcessorDatabase systemProcessorDatabase)
    {
        _systemProcessorDatabase = systemProcessorDatabase ?? throw new ArgumentNullException(nameof(systemProcessorDatabase), "System processor database cannot be null.");
        _context = context ?? throw new ArgumentNullException(nameof(context), "Context cannot be null.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger), "Logger cannot be null.");

        return InnerInit();
    }

    public abstract Task ProcessAsync();

    protected virtual Task InnerInit() => Task.CompletedTask;

    protected async Task DefaultNextSteps(ILLM llm, SystemProcessor systemProcessor, int maxSteps = 3)
    {
        Command[] commands = await llm.GetNextSteps(systemProcessor, Context);

        bool shouldContinue = true;
        do
        {
            if (maxSteps-- <= 0)
            {
                Logger.LogWarning("Max steps reached in DefaultNextSteps for character {CharacterId}, user {UserId}, {SystemProcessorName}",
                    Character.Id, User.Id, systemProcessor.Name);
                break;
            }

            foreach (Command command in commands)
            {
                await command.Execute(Context);

                if (command is MessageCommand response)
                {
                    await Communicator.SendMessage(User, Character, response.GetMessage());
                }

                shouldContinue &= command.ContinueAfterExecution;
            }
        } while (shouldContinue);
    }
}
