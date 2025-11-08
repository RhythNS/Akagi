using Akagi.Characters;
using Akagi.Characters.Conversations;
using Akagi.Data;
using Akagi.Receivers.Commands;
using Akagi.Receivers.MessageCompilers;
using Akagi.Users;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Akagi.Receivers.SystemProcessors;

internal class SystemProcessor : Savable
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _systemInstruction = string.Empty;
    private Message.Type _readableMessages = Message.Type.Character | Message.Type.User | Message.Type.System;
    private Message.Type _output = Message.Type.Character | Message.Type.User | Message.Type.System;
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

    [JsonIgnore]
    public MessageCompiler MessageCompiler
    {
        get => messageCompiler ?? throw new InvalidOperationException("MessageCompiler has not been initialized.");
        set => messageCompiler = value ?? throw new ArgumentNullException(nameof(value), "MessageCompiler cannot be null.");
    }

    [JsonIgnore]
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

        systemInstruction = systemInstruction.Replace("{{user}}", user.Name);
        systemInstruction = systemInstruction.Replace("{{character}}", character.Card.Name);
        systemInstruction = systemInstruction.Replace("{{description}}", character.Card.Description);

        return systemInstruction;
    }
}
