using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using CommunicationCommand = Akagi.Communication.Commands.Command;

namespace Akagi.Life.Japanese.Calls;

internal interface ICallChunkAnalyzer
{
    /// <summary>
    /// Analyzes one transcript chunk and records findings in the user's diagnosis. Returns the number of issues recorded.
    /// </summary>
    public Task<int> AnalyzeAsync(CallJob job, CallChunk chunk, CancellationToken cancellationToken);
}

/// <summary>
/// Lets the tutor character read a transcript chunk and file findings with its own diagnosis tools
/// (RecordJapaneseIssue, RecordCorrectUsage, RecordJapaneseStrength), so calls and chats feed the same profile.
/// </summary>
internal class ToolCallChunkAnalyzer : ICallChunkAnalyzer
{
    public const int MaxIssuesPerChunk = 6;

    public static readonly string[] Tools =
    [
        "RecordJapaneseIssue",
        "RecordCorrectUsage",
        "RecordJapaneseStrength",
        "StopExecution"
    ];

    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILLMFactory _llmFactory;
    private readonly ICommandFactory _commandFactory;
    private readonly ILogger _logger;

    public ToolCallChunkAnalyzer(IDatabaseFactory databaseFactory, ILLMFactory llmFactory, ICommandFactory commandFactory, ILogger logger)
    {
        _databaseFactory = databaseFactory;
        _llmFactory = llmFactory;
        _commandFactory = commandFactory;
        _logger = logger;
    }

    public async Task<int> AnalyzeAsync(CallJob job, CallChunk chunk, CancellationToken cancellationToken)
    {
        User user = await _databaseFactory.GetDatabase<IUserDatabase>().GetDocumentByIdAsync(job.UserId)
            ?? throw new InvalidOperationException($"User {job.UserId} not found.");
        string characterId = string.IsNullOrEmpty(job.AnalysisCharacterId) ? job.DeliveryCharacterId : job.AnalysisCharacterId;
        Character character = await _databaseFactory.GetDatabase<ICharacterDatabase>().GetCharacter(characterId)
            ?? throw new InvalidOperationException($"Analysis character {characterId} not found.");
        JapaneseDiagnosis? diagnosis = await _databaseFactory.GetDatabase<IJapaneseDiagnosisDatabase>().GetForUser(job.UserId);

        Conversation scratch = new() { Id = -1, Time = DateTime.UtcNow };
        scratch.AddMessage(new TextMessage
        {
            From = Message.Type.System,
            Time = DateTime.UtcNow,
            Text = BuildChunkMessage(job, chunk),
            CharacterId = null
        });

        SystemProcessor processor = new()
        {
            Name = "Call Analysis",
            Description = "Finds communication issues in a recorded call",
            RunMode = ILLM.RunMode.CommandsOnly,
            Usage = ILLM.LLMUsage.ReflectionGeneral,
            ReadableMessages = Message.Type.System,
            Output = Message.Type.System,
            MessageCompiler = new ContextOnlyConversationCompiler(),
            SystemInstruction = BuildInstruction(job, diagnosis, DateTime.UtcNow),
            CommandNames = Tools
        };
        await processor.Init([.. Tools.Select(_commandFactory.Create)], _databaseFactory);

        await using Context context = new()
        {
            Character = character,
            Conversation = scratch,
            User = user,
            Communicator = new SilentCommunicator(),
            LLMFactory = _llmFactory,
            DatabaseFactory = _databaseFactory
        };

        CallChunkBehavior behavior = new(processor) { MaxSteps = MaxIssuesPerChunk + 3 };
        await behavior.Init(_logger, context, _databaseFactory.GetDatabase<ISystemProcessorDatabase>());
        cancellationToken.ThrowIfCancellationRequested();
        await behavior.ProcessAsync();

        return scratch.Messages.OfType<CommandMessage>().Count(m => m.Command is RecordJapaneseIssueCommand);
    }

    internal static string BuildChunkMessage(CallJob job, CallChunk chunk)
    {
        string start = TimeSpan.FromSeconds(chunk.Start).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        string end = TimeSpan.FromSeconds(chunk.End).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        return $"Transcript part {chunk.Index + 1} of {job.Chunks.Count} ({start} to {end}):{Environment.NewLine}{chunk.Text}";
    }

    internal static string BuildInstruction(CallJob job, JapaneseDiagnosis? diagnosis, DateTime nowUtc)
    {
        string partner = string.IsNullOrWhiteSpace(job.Partner) ? "native Japanese speakers" : job.Partner;
        StringBuilder text = new();
        text.AppendLine($"You are {{{{character}}}}, {{{{user}}}}'s Japanese tutor. You are reviewing a transcript of a real conversation between {{{{user}}}} and {partner}.");
        text.AppendLine("{{user}} is a non-native speaker working towards natural, real-world Japanese around JLPT N2. The other speakers are native.");
        text.AppendLine(job switch
        {
            { LearnerChannel: not LearnerChannel.Mixed } => "Only {{user}}'s own audio channel was transcribed, so every line is theirs.",
            { LearnerSpeaker.Length: > 0 } => $"Lines labeled '{job.LearnerSpeaker}' are {{{{user}}}}; everything else is the other speakers.",
            _ => "Speakers are not labeled. Infer which lines are {{user}}'s (non-native phrasing, being addressed by name, context) and judge only those."
        });
        text.AppendLine($"Expected register for this relationship: {job.Register.ToString().ToLowerInvariant()}.");
        text.AppendLine();
        text.AppendLine("Look for, in {{user}}'s lines only:");
        text.AppendLine("- unnatural phrasing, textbook stiffness and odd collocations a native would not use");
        text.AppendLine("- particle slips");
        text.AppendLine("- politeness that does not fit the relationship (too stiff with friends, too casual otherwise)");
        text.AppendLine("- N2 grammar used wrongly or confused with a near neighbour");
        text.AppendLine("- hesitations, restarts, crutch words and the same sentence frame over and over");
        text.AppendLine();
        text.AppendLine($"Call RecordJapaneseIssue for at most {MaxIssuesPerChunk} real and instructive issues: Said verbatim, Better as a native would say it in this situation, a one sentence Explanation, Severity, and Source '{job.SourceLabel}'.");
        text.AppendLine("Ignore obvious transcription errors, single slips of the tongue and the other speakers' speech.");
        if (diagnosis?.Focus(8, nowUtc) is { Count: > 0 } focus)
        {
            text.AppendLine("Known focus areas. Call RecordCorrectUsage when {{user}} gets one of these right, and reuse the same Pattern when it goes wrong again:");
            foreach (DiagnosisIssue issue in focus)
            {
                text.AppendLine($"- [{issue.Id}] {issue.Category}: {issue.Pattern}");
            }
        }
        text.AppendLine("Call RecordJapaneseStrength once if {{user}} does something genuinely well.");
        text.AppendLine("N2 nuance groups learners confuse:");
        foreach (string group in GrammarPatterns.DescribeGroups())
        {
            text.AppendLine("- " + group);
        }
        text.Append("When you are done with this part, call StopExecution.");
        return text.ToString();
    }

    /// <summary>
    /// Runs the tool loop of <see cref="CharacterBehavior"/> for a processor built on the fly.
    /// </summary>
    private sealed class CallChunkBehavior : CharacterBehavior
    {
        private readonly SystemProcessor _processor;

        public CallChunkBehavior(SystemProcessor processor)
        {
            _processor = processor;
        }

        public override async Task ProcessAsync()
        {
            ILLM llm = await LLMFactory.Create(User, _processor.SpecificLLM, _processor.Usage);
            await DefaultNextSteps(llm, _processor);
        }
    }

    /// <summary>
    /// Analysis runs in the background; nothing it produces is sent to the user directly.
    /// </summary>
    private sealed class SilentCommunicator : ICommunicator
    {
        public string Name => "call-analysis";
        public CommunicationCommand[] AvailableCommands => [];
        public Task SendMessage(User user, Character character, string message) => Task.CompletedTask;
        public Task SendMessage(User user, Character character, Message message) => Task.CompletedTask;
        public Task SendMessage(User user, string message) => Task.CompletedTask;
        public Task SendMessage(User user, Message message) => Task.CompletedTask;
        public Task SendAudio(User user, Character character, Stream stream, string fileName) => Task.CompletedTask;
        public Task SendAudio(User user, Stream stream, string fileName) => Task.CompletedTask;
    }
}
