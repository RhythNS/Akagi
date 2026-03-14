using Akagi.Bridge.Attributes;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.LLMs;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Interceptors;

internal class ProcessingInterceptor : Interceptor
{
    [Flags]
    public enum ProcessingGuard
    {
        AutomaticProcessing = 1,
        Voice = 2,
    }

    private string _systemProcessorId = string.Empty;
    private bool _passThrough = false;
    private ProcessingGuard _passThroughGuard;

    [NodeReference(typeof(SystemProcessor))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string SystemProcessorId
    {
        get => _systemProcessorId;
        set => SetProperty(ref _systemProcessorId, value);
    }
    public bool PassThrough
    {
        get => _passThrough;
        set => SetProperty(ref _passThrough, value);
    }
    public ProcessingGuard PassThroughGuard
    {
        get => _passThroughGuard;
        set => SetProperty(ref _passThroughGuard, value);
    }

    private SystemProcessor? _systemProcessor;

    protected override async Task InnerInit()
    {
        _systemProcessor = await SystemProcessorDatabase.GetSystemProcessor(SystemProcessorId);
    }

    public override async Task SendMessageAsync(User user, Character character, Message message,
        Func<User, Character, Message, Task> next)
    {
        if (_systemProcessor == null)
        {
            throw new InvalidOperationException($"System processor with ID {SystemProcessorId} not found.");
        }
        if (PassThroughGuard.HasFlag(ProcessingGuard.AutomaticProcessing) && character.AllowAutomaticProcessing == false)
        {
            await next(user, character, message);
            return;
        }
        if (PassThroughGuard.HasFlag(ProcessingGuard.Voice) && character.AllowVoice == false)
        {
            await next(user, character, message);
            return;
        }

        if (PassThrough)
        {
            await next(user, character, message);
        }

        ILLM llm = await LLMFactory.Create(User, _systemProcessor.SpecificLLM, _systemProcessor.Usage);
        InterceptingCommunicator? communicator = Context.Communicator as InterceptingCommunicator
            ?? throw new InvalidOperationException("Context communicator is not an InterceptingCommunicator.");
        communicator = communicator.CreateFrom(this);
        var newContext = new Receivers.Context
        {
            Character = Context.Character,
            Communicator = communicator,
            Conversation = Context.Conversation,
            DatabaseFactory = Context.DatabaseFactory,
            LLMFactory = Context.LLMFactory,
            User = Context.User
        };
        Command[] commands = await llm.GetNextSteps(_systemProcessor, newContext);
        await ExecuteCommandsAsync(commands, communicator, newContext);
    }

    private async Task ExecuteCommandsAsync(Command[] commands, ICommunicator communicator, Receivers.Context context)
    {
        foreach (Command command in commands)
        {
            Command[] next = await command.Execute(context);
            if (command is MessageCommand response)
            {
                await communicator.SendMessage(User, Character, response.GetMessage());
            }

            if (next.Length > 0)
            {
                await ExecuteCommandsAsync(next, communicator, context);
            }
        }
    }
}
