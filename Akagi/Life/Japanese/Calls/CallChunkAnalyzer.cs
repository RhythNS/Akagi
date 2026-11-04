using Akagi.Characters;
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

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// What one chunk's review noted. Nothing in it has reached the diagnosis yet.
/// </summary>
internal record ChunkReview(List<CallFinding> Findings, string Summary);

internal record FindingGroup(string[] FindingIds, string IssueId, string Category, string Pattern, string Explanation, int Severity);

/// <summary>
/// The consolidation's answer: which findings are one problem, which do not count, and the review of the whole call.
/// </summary>
internal class Consolidation
{
    public List<FindingGroup> Groups { get; } = [];
    public List<(string[] FindingIds, string Reason)> Drops { get; } = [];
    public string Summary { get; set; } = string.Empty;
    public List<string> Priorities { get; set; } = [];

    public bool IsEmpty => Groups.Count == 0 && Drops.Count == 0 && Summary.Length == 0;
}

internal interface ICallChunkAnalyzer
{
    /// <summary>
    /// Reviews one transcript chunk and returns what it noted, without touching the diagnosis.
    /// </summary>
    public Task<ChunkReview> AnalyzeAsync(CallJob job, CallChunk chunk, JapaneseDiagnosis? diagnosis, CancellationToken cancellationToken);
}

internal interface ICallConsolidator
{
    /// <summary>
    /// Looks at every finding of the call at once. Null when the request gave nothing usable.
    /// </summary>
    public Task<Consolidation?> ConsolidateAsync(CallJob job, JapaneseDiagnosis? diagnosis, CancellationToken cancellationToken);
}

/// <summary>
/// The tutor character reviews the call on the Learning model: each chunk with the story so far, then one request over all
/// findings. Its tools only carry arguments; the pipeline checks quotes and decides what reaches the diagnosis.
/// </summary>
internal class ToolCallReviewer : ICallChunkAnalyzer, ICallConsolidator
{
    public const int MaxMistakesPerChunk = 12;
    public const int MaxComprehensionIssuesPerChunk = 3;
    public const int MaxPhrasesPerChunk = 3;
    public const int MaxKnownIssues = 60;
    public const int ContextLines = 6;

    public static readonly string[] ChunkTools =
    [
        "NoteCallMistake",
        "NoteCallCorrectUse",
        "NoteCallStrength",
        "NoteCallPhrase",
        "NoteCallSummary",
        "StopExecution"
    ];

    public static readonly string[] ConsolidationTools =
    [
        "GroupCallFindings",
        "DropCallFindings",
        "WriteCallReview"
    ];

    internal const string SeverityScale =
        "Severity: 3 = a native would misunderstand, be confused, or find it rude; 2 = understood, but clearly not how a native says it; " +
        "1 = small polish a native might not even notice.";

    private readonly IDatabaseFactory _databaseFactory;
    private readonly ILLMFactory _llmFactory;
    private readonly ICommandFactory _commandFactory;
    private readonly ILogger _logger;

    public ToolCallReviewer(IDatabaseFactory databaseFactory, ILLMFactory llmFactory, ICommandFactory commandFactory, ILogger logger)
    {
        _databaseFactory = databaseFactory;
        _llmFactory = llmFactory;
        _commandFactory = commandFactory;
        _logger = logger;
    }

    public async Task<ChunkReview> AnalyzeAsync(CallJob job, CallChunk chunk, JapaneseDiagnosis? diagnosis, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Call {JobId}: chunk {Chunk} goes to the tutor on the Learning model", job.Id, chunk.Index + 1);
        Command[] calls = await RequestAsync(job, "Call Review", BuildInstruction(job, diagnosis, DateTime.UtcNow), BuildChunkMessage(job, chunk),
            ChunkTools, cancellationToken);
        if (calls.Length == 0)
        {
            _logger.LogWarning("Review of chunk {Chunk} of call {JobId} returned no tool calls", chunk.Index, job.Id);
        }
        return ReadChunk(calls, chunk.Index);
    }

    public async Task<Consolidation?> ConsolidateAsync(CallJob job, JapaneseDiagnosis? diagnosis, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Call {JobId}: consolidating {Findings} findings", job.Id, job.Findings.Count);
        Command[] calls = await RequestAsync(job, "Call Consolidation", BuildConsolidationInstruction(job), BuildConsolidationMessage(job, diagnosis),
            ConsolidationTools, cancellationToken);
        Consolidation result = ReadConsolidation(calls);
        return result.IsEmpty ? null : result;
    }

    internal static ChunkReview ReadChunk(IEnumerable<Command> calls, int chunkIndex)
    {
        List<CallFinding> findings = [];
        List<string> summaries = [];
        foreach (Command call in calls)
        {
            switch (call)
            {
                case NoteCallMistakeCommand mistake when mistake.Pattern.Length > 0:
                    findings.Add(new CallFinding
                    {
                        Kind = FindingKind.Mistake,
                        Category = JapaneseCommand.ParseCategory(mistake.Category) ?? IssueCategory.Other,
                        Pattern = mistake.Pattern,
                        IssueId = mistake.IssueId,
                        Said = mistake.Said,
                        Better = mistake.Better,
                        Explanation = mistake.Explanation,
                        Severity = mistake.Severity,
                        ChunkIndex = chunkIndex
                    });
                    break;

                case NoteCallCorrectUseCommand correct when correct.IssueId.Length > 0:
                    findings.Add(new CallFinding { Kind = FindingKind.Correct, IssueId = correct.IssueId, Said = correct.Said, ChunkIndex = chunkIndex });
                    break;

                case NoteCallStrengthCommand strength when strength.Strength.Length > 0:
                    findings.Add(new CallFinding { Kind = FindingKind.Strength, Pattern = strength.Strength, Said = strength.Said, ChunkIndex = chunkIndex });
                    break;

                case NoteCallPhraseCommand phrase when phrase.Phrase.Length > 0:
                    findings.Add(new CallFinding { Kind = FindingKind.Phrase, Said = phrase.Phrase, Better = phrase.Meaning, ChunkIndex = chunkIndex });
                    break;

                case NoteCallSummaryCommand summary when summary.Summary.Length > 0:
                    summaries.Add(summary.Summary);
                    break;
            }
        }
        return new ChunkReview(findings, string.Join(" ", summaries));
    }

    internal static Consolidation ReadConsolidation(IEnumerable<Command> calls)
    {
        Consolidation result = new();
        foreach (Command call in calls)
        {
            switch (call)
            {
                case GroupCallFindingsCommand group when group.FindingIds.Length > 0:
                    result.Groups.Add(new FindingGroup(group.FindingIds, group.IssueId, group.Category, group.Pattern, group.Explanation, group.Severity));
                    break;
                case DropCallFindingsCommand drop when drop.FindingIds.Length > 0:
                    result.Drops.Add((drop.FindingIds, drop.Reason));
                    break;
                case WriteCallReviewCommand review:
                    result.Summary = review.Summary;
                    result.Priorities = [.. review.Priorities];
                    break;
            }
        }
        return result;
    }

    private async Task<Command[]> RequestAsync(CallJob job, string name, string instruction, string message, string[] tools, CancellationToken cancellationToken)
    {
        User user = await _databaseFactory.GetDatabase<IUserDatabase>().GetDocumentByIdAsync(job.UserId)
            ?? throw new InvalidOperationException($"User {job.UserId} not found.");
        string characterId = string.IsNullOrEmpty(job.AnalysisCharacterId) ? job.DeliveryCharacterId : job.AnalysisCharacterId;
        Character character = await _databaseFactory.GetDatabase<ICharacterDatabase>().GetCharacter(characterId)
            ?? throw new InvalidOperationException($"Analysis character {characterId} not found.");

        Conversation scratch = new() { Id = -1, Time = DateTime.UtcNow };
        scratch.AddMessage(new TextMessage
        {
            From = Message.Type.System,
            Time = DateTime.UtcNow,
            Text = message,
            CharacterId = null
        });

        SystemProcessor processor = new()
        {
            Name = name,
            Description = "Reviews a recorded call",
            RunMode = ILLM.RunMode.CommandsOnly,
            // Reviewing the learner's Japanese is tutoring, so it runs on the model chosen for lessons
            Usage = ILLM.LLMUsage.Learning,
            ReadableMessages = Message.Type.System,
            Output = Message.Type.System,
            MessageCompiler = new ContextOnlyConversationCompiler(),
            SystemInstruction = instruction,
            CommandNames = tools
        };
        await processor.Init([.. tools.Select(_commandFactory.Create)], _databaseFactory);

        await using Context context = new()
        {
            Character = character,
            Conversation = scratch,
            User = user,
            Communicator = new SilentCommunicator(),
            LLMFactory = _llmFactory,
            DatabaseFactory = _databaseFactory
        };

        cancellationToken.ThrowIfCancellationRequested();

        // One request holding every call: Gemini 3 models reject a replayed tool call unless its reasoning details are
        // sent back, which conversations do not store, so this must not continue a tool exchange
        ILLM llm = await _llmFactory.Create(user, processor.SpecificLLM, processor.Usage);
        return await llm.GetNextSteps(processor, context);
    }

    internal static string BuildChunkMessage(CallJob job, CallChunk chunk)
    {
        StringBuilder text = new();
        List<CallChunk> earlier = [.. job.Chunks.Where(c => c.Index < chunk.Index && c.Summary.Length > 0)];
        if (earlier.Count > 0)
        {
            text.AppendLine("The story so far, from the earlier parts:");
            foreach (CallChunk previous in earlier)
            {
                text.AppendLine($"- Part {previous.Index + 1}: {previous.Summary}");
            }
            text.AppendLine();
        }

        if (chunk.Index > 0 && chunk.Index - 1 < job.Chunks.Count)
        {
            string[] lines = job.Chunks[chunk.Index - 1].Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                text.AppendLine("The end of the previous part, for context only. It was already reviewed: note nothing from these lines.");
                foreach (string line in lines.TakeLast(ContextLines))
                {
                    text.AppendLine(line.TrimEnd('\r'));
                }
                text.AppendLine();
            }
        }

        string start = TimeSpan.FromSeconds(chunk.Start).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        string end = TimeSpan.FromSeconds(chunk.End).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        text.Append($"Transcript part {chunk.Index + 1} of {job.Chunks.Count} ({start} to {end}):{Environment.NewLine}{chunk.Text}");
        return text.ToString();
    }

    internal static string BuildInstruction(CallJob job, JapaneseDiagnosis? diagnosis, DateTime nowUtc)
    {
        string partner = string.IsNullOrWhiteSpace(job.Partner) ? "native Japanese speakers" : job.Partner;
        StringBuilder text = new();
        text.AppendLine($"You are {{{{character}}}}, {{{{user}}}}'s Japanese tutor. You are reviewing a transcript of a real conversation between {{{{user}}}} and {partner}, one part at a time.");
        text.AppendLine("{{user}} is a non-native speaker working towards natural, real-world Japanese around JLPT N2. The other speakers are native.");
        text.AppendLine(job switch
        {
            { HasBothVoices: true } => $"Lines labeled '{job.LearnerSpeaker}' are {{{{user}}}} and lines labeled '{job.PartnerLabel}' are {partner}. Each voice was recorded on its own channel, so these labels are reliable. Overlapping lines are ordered by when they start.",
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
        text.AppendLine($"Call NoteCallMistake once for every real and instructive issue, up to {MaxMistakesPerChunk}. Note each occurrence separately, even when the same problem comes up again: the count matters.");
        text.AppendLine("Said must be copied character for character from one of {{user}}'s lines: the quotes are checked against the transcript, and a finding whose words are not there is thrown away.");
        text.AppendLine("Better is what a native would say in this situation, and Explanation one sentence on why.");
        text.AppendLine(SeverityScale);
        text.AppendLine("Ignore obvious transcription errors, single slips of the tongue and the other speakers' speech. Things natives say just as casually are not mistakes.");
        if (job.HasBothVoices)
        {
            text.AppendLine();
            text.AppendLine($"Because {partner}'s side is here too, first look for comprehension breakdowns: {{{{user}}}} answering something other than what was asked, {partner} having to repeat, rephrase or ask back (ん？), or {{{{user}}}} repeating a word wrongly. Note up to {MaxComprehensionIssuesPerChunk} of these on top of the limit above; they are what a review of one voice could never show.");
            text.AppendLine("Use Category Comprehension for these: Pattern such as 'heard ちいかわ as ちかわ', Said with {{user}}'s own line, Better with a natural reply, and an Explanation quoting the evidence, e.g. 'She asked X and you answered as if she had said Y'.");
            text.AppendLine("A transcript cannot prove what {{user}} heard. Only report what the conversation itself shows, and write 'seems to have', never that they did.");
            text.AppendLine();
            text.AppendLine("Some lines end with (second hearing: A -> B): a second transcription of the same moment has B where the line has A. Either may be what was really said, and for {{user}}'s habits the line itself is usually closer to the actual words.");
            text.AppendLine("If a finding involves A or B, its Explanation must begin with 'Possibly:'. A disputed word alone never shows that {{user}} misheard or misused something, but a reaction in the conversation does: a Comprehension issue with a real reaction stays, with 'Possibly:' if its words are disputed.");
            text.AppendLine();
            text.AppendLine($"Call NoteCallPhrase for up to {MaxPhrasesPerChunk} natural, reusable expressions {partner} used that {{{{user}}}} could adopt: everyday spoken Japanese a textbook does not teach, copied exactly from {partner}'s lines.");
        }

        List<DiagnosisIssue> known = diagnosis == null ? [] : [.. diagnosis.Issues
            .Where(i => i.Status != IssueStatus.Resolved)
            .OrderByDescending(i => i.Weight(nowUtc))
            .Take(MaxKnownIssues)];
        if (known.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Known issues of {{user}}. When a mistake is one of these, pass its IssueId and reuse its pattern. When {{user}} gets one of them right, call NoteCallCorrectUse with its id and their words:");
            foreach (DiagnosisIssue issue in known)
            {
                text.AppendLine($"- [{issue.Id}] {issue.Category}: {issue.Pattern}");
            }
        }
        text.AppendLine();
        text.AppendLine("Call NoteCallStrength once if {{user}} does something genuinely well, with the words that show it.");
        text.AppendLine("Always call NoteCallSummary once: what this part was about, for the parts after it.");
        text.AppendLine("N2 nuance groups learners confuse:");
        foreach (string group in GrammarPatterns.DescribeGroups())
        {
            text.AppendLine("- " + group);
        }
        text.Append("Make all your calls in this one response, there is no second turn.");
        return text.ToString();
    }

    internal static string BuildConsolidationInstruction(CallJob job)
    {
        string partner = string.IsNullOrWhiteSpace(job.Partner) ? "a native speaker" : job.Partner;
        StringBuilder text = new();
        text.AppendLine($"You are {{{{character}}}}, {{{{user}}}}'s Japanese tutor. You reviewed a call between {{{{user}}}} and {partner} part by part; below is everything you noted. Now look at the call as a whole.");
        text.AppendLine();
        text.AppendLine("1. Call GroupCallFindings for the mistakes: findings that are the same underlying problem go into one group, even when they were worded differently in different parts. Every mistake you keep must be in exactly one group; a finding may be alone in its group.");
        text.AppendLine("   When a group is one of the known issues, pass its IssueId and its pattern. Otherwise give a short canonical Pattern a learner recognizes, e.g. 'に vs で for location of action'.");
        text.AppendLine("   Set one Severity for the group on this scale. " + SeverityScale);
        text.AppendLine("2. Call DropCallFindings for anything that should not count: not actually a mistake, something natives say the same way, a likely transcription error, or a duplicate of another finding about the very same words. Give a short reason the learner will read. Dropping a finding wrongly costs the learner a lesson, so only drop what you are sure about.");
        text.AppendLine("   You may also drop correct uses, strengths or phrases that are wrong or repeated.");
        text.AppendLine("3. Call WriteCallReview once: an honest, balanced summary of how {{user}} did, and up to three priorities ranked by how much they would improve real conversations, each with how to practice it.");
        text.AppendLine();
        text.Append("Findings marked 'possibly' have disputed words; keep them only when the conversation itself supports them. Make all your calls in this one response, there is no second turn.");
        return text.ToString();
    }

    internal static string BuildConsolidationMessage(CallJob job, JapaneseDiagnosis? diagnosis)
    {
        StringBuilder text = new();
        List<CallChunk> summarized = [.. job.Chunks.Where(c => c.Summary.Length > 0)];
        if (summarized.Count > 0)
        {
            text.AppendLine("What the call was about:");
            foreach (CallChunk chunk in summarized)
            {
                text.AppendLine($"- Part {chunk.Index + 1}: {chunk.Summary}");
            }
            text.AppendLine();
        }

        List<CallFinding> open = [.. job.Findings.Where(f => f.State == FindingState.Candidate)];
        AppendFindings(text, "Mistakes ([id] part time | category | pattern | said -> better | explanation | severity):",
            open.Where(f => f.Kind == FindingKind.Mistake),
            f => $"{f.Category} | {f.Pattern}{(f.IssueId.Length > 0 ? $" (known issue {f.IssueId})" : string.Empty)} | {f.Said} -> {f.Better} | {f.Explanation} | {f.Severity}");
        AppendFindings(text, "Known issues done right ([id] part time | issue | said):",
            open.Where(f => f.Kind == FindingKind.Correct), f => $"{f.IssueId} | {f.Said}");
        AppendFindings(text, "Strengths ([id] part time | strength | said):",
            open.Where(f => f.Kind == FindingKind.Strength), f => $"{f.Pattern} | {f.Said}");
        AppendFindings(text, "Phrases worth adopting ([id] part time | phrase | meaning):",
            open.Where(f => f.Kind == FindingKind.Phrase), f => $"{f.Said} | {f.Better}");

        int rejected = job.Findings.Count(f => f.State == FindingState.Rejected);
        if (rejected > 0)
        {
            text.AppendLine($"({rejected} more were thrown away because their quotes are not in {{{{user}}}}'s lines.)");
            text.AppendLine();
        }

        if (diagnosis?.Issues.Count > 0)
        {
            text.AppendLine("Known issues:");
            foreach (DiagnosisIssue issue in diagnosis.Issues.Where(i => i.Status != IssueStatus.Resolved).OrderByDescending(i => i.Weight(DateTime.UtcNow)).Take(MaxKnownIssues))
            {
                text.AppendLine($"- [{issue.Id}] {issue.Category}: {issue.Pattern} ({issue.Count}x so far)");
            }
        }
        return text.ToString().TrimEnd();
    }

    private static void AppendFindings(StringBuilder text, string title, IEnumerable<CallFinding> findings, Func<CallFinding, string> describe)
    {
        List<CallFinding> list = [.. findings];
        if (list.Count == 0)
        {
            return;
        }
        text.AppendLine(title);
        foreach (CallFinding finding in list)
        {
            string time = finding.Time is double seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "--:--:--";
            string possibly = finding.Quote == QuoteCheck.Disputed ? " (possibly)" : string.Empty;
            text.AppendLine($"- [{finding.Id}] part {finding.ChunkIndex + 1} {time}{possibly} | {describe(finding)}");
        }
        text.AppendLine();
    }
}
