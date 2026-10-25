using Akagi.Characters.CharacterBehaviors.Interceptors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
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
    private int _maxSteps = 3;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    public int MaxSteps
    {
        get => _maxSteps;
        set => SetProperty(ref _maxSteps, value);
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

    protected async Task DefaultNextSteps(ILLM llm, SystemProcessor systemProcessor)
    {
        bool shouldContinue;
        int maxSteps = MaxSteps;

        do
        {
            if (maxSteps-- <= 0)
            {
                Logger.LogWarning("Max steps reached in DefaultNextSteps for character {CharacterId}, user {UserId}, {SystemProcessorName}",
                    Character.Id, User.Id, systemProcessor.Name);
                break;
            }

            bool canStream = Communicator is IStreamingCommunicator
                && systemProcessor.RunMode != ILLM.RunMode.CommandsOnly
                && systemProcessor.Usage != ILLM.LLMUsage.ReflectionRoleplay
                && systemProcessor.Usage != ILLM.LLMUsage.ReflectionGeneral
                && systemProcessor.Output != Message.Type.System;

            if (canStream)
            {
                shouldContinue = await ExecuteStreamingStepAsync(llm, systemProcessor);
            }
            else
            {
                Command[] commands = await llm.GetNextSteps(systemProcessor, Context);

                if (commands.Length == 0)
                {
                    Logger.LogWarning("No commands returned from LLM for character {CharacterId}, user {UserId}, {SystemProcessorName}",
                        Character.Id, User.Id, systemProcessor.Name);
                    return;
                }

                shouldContinue = await ExecuteCommandsAsync(commands, isStreamed: false);
            }
        } while (shouldContinue);
    }

    private async Task<bool> ExecuteStreamingStepAsync(ILLM llm, SystemProcessor systemProcessor)
    {
        IStreamingCommunicator streamingCommunicator = (IStreamingCommunicator)Communicator;
        await using IMessageStream stream = await streamingCommunicator.OpenMessageStreamAsync(User, Character);

        try
        {
            Command[] finalCommands = [];
            string fullText = string.Empty;

            await foreach (LLMStreamEvent streamEvent in llm.GetNextStepsStreamAsync(systemProcessor, Context))
            {
                switch (streamEvent)
                {
                    case TextDeltaEvent textDelta:
                        await stream.WriteChunkAsync(textDelta.Text);
                        break;

                    case StreamCompleteEvent complete:
                        finalCommands = complete.FinalCommands;
                        fullText = complete.CompleteText;
                        break;
                }
            }

            await stream.CompleteAsync(fullText);

            if (finalCommands.Length == 0)
            {
                Logger.LogWarning("No commands returned from LLM stream for character {CharacterId}, user {UserId}, {SystemProcessorName}",
                    Character.Id, User.Id, systemProcessor.Name);
                return false;
            }

            return await ExecuteCommandsAsync(finalCommands, isStreamed: true);
        }
        catch (Exception ex)
        {
            await stream.AbortAsync(ex);
            throw;
        }
    }

    private async Task<bool> ExecuteCommandsAsync(Command[] commands, bool isStreamed = false)
    {
        bool shouldContinue = true;

        foreach (Command command in commands)
        {
            Command[] next = await command.Execute(Context);

            if (command is MessageCommand response)
            {
                if (isStreamed)
                {
                    if (Communicator is InterceptingCommunicator interceptingComm)
                    {
                        await interceptingComm.DispatchStreamCompletedMessageAsync(User, Character, response.GetMessage());
                    }
                }
                else
                {
                    await Communicator.SendMessage(User, Character, response.GetMessage());
                }
            }

            shouldContinue &= command.ContinueAfterExecution;

            if (next.Length > 0)
            {
                shouldContinue &= await ExecuteCommandsAsync(next, isStreamed: false);
            }
        }

        return shouldContinue;
    }
}
