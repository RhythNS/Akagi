using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers.Commands;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.SystemProcessors;

internal interface ISystemProcessorDatabase : IDatabase<SystemProcessor>
{
    public Task<SystemProcessor[]> GetSystemProcessor(string[] ids);
    public Task<SystemProcessor> GetSystemProcessor(string id);
}

internal class SystemProcessor : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _systemInstruction = string.Empty;
    private Message.Type _readableMessages = Message.Type.Character | Message.Type.User | Message.Type.System;
    private Message.Type _output = Message.Type.Character | Message.Type.User | Message.Type.System;
    private ILLM.RunMode _runMode = ILLM.RunMode.Mixed;
    private LLMDefinition? _specificLLM = null;
    private string _messageCompilerId = string.Empty;
    private string[] _commandNames = [];

    private MessageCompiler? messageCompiler;

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
    public string SystemInstruction
    {
        get => _systemInstruction;
        set => SetProperty(ref _systemInstruction, value);
    }
    public Message.Type ReadableMessages
    {
        get => _readableMessages;
        set => SetProperty(ref _readableMessages, value);
    }
    public Message.Type Output
    {
        get => _output;
        set => SetProperty(ref _output, value);
    }
    public ILLM.RunMode RunMode
    {
        get => _runMode;
        set => SetProperty(ref _runMode, value);
    }
    public LLMDefinition? SpecificLLM
    {
        get => _specificLLM;
        set => SetProperty(ref _specificLLM, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string MessageCompilerId
    {
        get => _messageCompilerId;
        set => SetProperty(ref _messageCompilerId, value);
    }
    public string[] CommandNames
    {
        get => _commandNames;
        set => SetProperty(ref _commandNames, value);
    }

    [BsonIgnore]
    public MessageCompiler MessageCompiler
    {
        get => messageCompiler ?? throw new InvalidOperationException("MessageCompiler has not been initialized.");
        set => messageCompiler = value ?? throw new ArgumentNullException(nameof(value), "MessageCompiler cannot be null.");
    }

    [BsonIgnore]
    public Command[] Commands { get; set; } = [];

    public async Task Init(Command[] commands, IDatabaseFactory databaseFactory)
    {
        if (commands.Length != CommandNames.Length)
        {
            throw new ArgumentException("Command names and command instances must have the same length.");
        }

        Commands = commands;

        await MessageCompiler.Init(ReadableMessages, databaseFactory.GetDatabase<IMessageCompilerDatabase>());
    }

    public virtual string CompileSystemPrompt(User user, Character character)
    {
        string systemInstruction = SystemInstruction;

        Dictionary<string, string> replacements = new()
        {
            { "{{user}}", user.Name },
            { "{{character}}", character.Card.Name },
        };

        string description = character.Card.Description;
        foreach ((string? key, string? value) in replacements)
        {
            description = description.Replace(key, value);
        }
        replacements.Add("{{description}}", description);

        foreach ((string? key, string? value) in replacements)
        {
            systemInstruction = systemInstruction.Replace(key, value);
        }

        return systemInstruction;
    }
}
