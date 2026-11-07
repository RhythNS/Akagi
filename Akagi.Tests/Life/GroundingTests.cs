using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Groups;
using Akagi.Characters.Routines;
using Akagi.Life;
using Akagi.Life.Mood;
using Akagi.Life.Work;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Akagi.Receivers.Commands.Messages;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using System.Runtime.CompilerServices;

namespace Akagi.Tests.Life;

public class GroundingTests
{
    [Theory]
    [InlineData("so tired", true)]
    [InlineData("SO TIRED!!", true)]
    [InlineData("work ... so tired", true)]
    [InlineData("exhausted", false)]
    [InlineData("ok", false)]
    [InlineData("", false)]
    public void IsQuoted_MatchesHisWordsIgnoringCaseAndPunctuation(string evidence, bool expected)
    {
        Assert.Equal(expected, Grounding.IsQuoted(evidence, ["Ugh, work was long and I am so tired."]));
    }

    [Fact]
    public void IsQuoted_AcceptsShortJapaneseAndFullWidth()
    {
        Assert.True(Grounding.IsQuoted("眠い", ["今日はめっちゃ眠い"]));
        Assert.True(Grounding.IsQuoted("ＡＢＣテスト", ["明日abcテストがある"]));
    }

    [Fact]
    public void Similarity_SeesRewordedRepeatsButNotDifferentThings()
    {
        Assert.True(Grounding.Similarity(
            "tired after day 4 back at work - the short week only just started for him",
            "tired after day 4 back at work - the short week only just started for him, wanted to lie down") > 0.75);
        Assert.True(Grounding.Similarity("nervous about the dentist", "excited for the concert on Friday") < 0.4);
    }

    [Fact]
    public void RecentUserTexts_TakesOnlyRecentUserMessagesNewestFirst()
    {
        DateTime now = DateTime.UtcNow;
        Message[] messages =
        [
            new TextMessage { CharacterId = null, From = Message.Type.User, Time = now.AddDays(-5), Text = "old" },
            new TextMessage { CharacterId = null, From = Message.Type.User, Time = now.AddMinutes(-5), Text = "first" },
            new TextMessage { CharacterId = null, From = Message.Type.Character, Time = now.AddMinutes(-4), Text = "her reply" },
            new TextMessage { CharacterId = null, From = Message.Type.System, Time = now.AddMinutes(-3), Text = "[System] [Proactive] ..." },
            new TextMessage { CharacterId = null, From = Message.Type.User, Time = now.AddMinutes(-1), Text = "second" },
        ];

        Assert.Equal(["second", "first"], Grounding.RecentUserTexts(messages, now));
    }

    [Fact]
    public void Guard_RefusesStateToolsOnSystemAndConsultationTurnsButAllowsReads()
    {
        LifeTestContext test = new();
        test.Conversation.AddMessage(UserSays("I have my driving exam on Friday"));
        Command record = Tool<RecordMoodCommand>(("Note", "driving exam on Friday"), ("Kind", "event"), ("Evidence", "driving exam on Friday"));

        test.Context.Turn = TurnSource.System;
        Assert.StartsWith(ToolGuard.RefusalPrefix, new ToolGuard().Check(record, test.Context));
        Assert.Null(new ToolGuard().Check(Tool<ListRoutinesCommand>(), test.Context));

        test.Context.Turn = TurnSource.Consultation;
        Assert.StartsWith(ToolGuard.RefusalPrefix, new ToolGuard().Check(record, test.Context));

        test.Context.Turn = TurnSource.User;
        Assert.Null(new ToolGuard().Check(record, test.Context));

        // Callers that do not say what started the turn (pipelines, tests) keep working as before
        test.Context.Turn = TurnSource.Unknown;
        Assert.Null(new ToolGuard().Check(Tool<RecordMoodCommand>(("Note", "made up")), test.Context));
    }

    [Fact]
    public void Guard_NeedsEvidenceFromHisMessages()
    {
        LifeTestContext test = new();
        test.Context.Turn = TurnSource.User;
        test.Conversation.AddMessage(UserSays("Hello can you hear me?"));
        test.Conversation.AddMessage(new TextMessage
        {
            From = Message.Type.Character,
            CharacterId = "akagi",
            Time = DateTime.UtcNow,
            Text = "You're moving to Japan on 17 Dec, visa and apartment confirmed!"
        });

        string? missing = new ToolGuard().Check(Tool<RecordMoodCommand>(("Note", "moving to Japan")), test.Context);
        string? invented = new ToolGuard().Check(Tool<RecordMoodCommand>(("Note", "moving to Japan"), ("Evidence", "moving to Japan on 17 Dec")), test.Context);

        Assert.Contains("Evidence", missing);
        Assert.Contains("is not in any of his recent messages", invented);
    }

    [Fact]
    public void Guard_RefusesWhenHeHasNotWrittenAnything()
    {
        LifeTestContext test = new();
        test.Context.Turn = TurnSource.Reflection;

        string? refusal = new ToolGuard().Check(Tool<RecordMoodCommand>(("Note", "tired"), ("Evidence", "so tired")), test.Context);

        Assert.Contains("has not written anything", refusal);
    }

    [Fact]
    public void Guard_RefusesTheSameWordsTwiceForOneTool()
    {
        LifeTestContext test = new();
        test.Context.Turn = TurnSource.User;
        test.Conversation.AddMessage(UserSays("I'm so tired today"));
        Command first = Tool<RecordMoodCommand>(("Note", "tired"), ("Evidence", "so tired"));
        test.Conversation.AddMessage(new CommandMessage { CharacterId = null, From = Message.Type.Character, Command = first, Output = "Recorded checkin: tired", Time = DateTime.UtcNow });

        string? again = new ToolGuard().Check(Tool<RecordMoodCommand>(("Note", "exhausted"), ("Evidence", "So tired")), test.Context);
        string? otherTool = new ToolGuard().Check(Tool<LogSleepCommand>(("Hours", "5"), ("Evidence", "so tired")), test.Context);

        Assert.Contains("already done for these words", again);
        Assert.Null(otherTool);
    }

    [Fact]
    public void Guard_LimitsChangesPerStepAndSkipsIdenticalCalls()
    {
        LifeTestContext test = new();
        test.Context.Turn = TurnSource.User;
        test.Conversation.AddMessage(UserSays("Exam on Friday, dentist on Monday, gym tonight and a call with Rii tomorrow"));
        ToolGuard guard = new();
        guard.BeginStep();

        Assert.Null(guard.Check(Tool<RecordMoodCommand>(("Note", "exam"), ("Evidence", "Exam on Friday")), test.Context));
        Assert.Contains("identical", guard.Check(Tool<RecordMoodCommand>(("Note", "exam"), ("Evidence", "Exam on Friday")), test.Context));
        Assert.Null(guard.Check(Tool<RecordMoodCommand>(("Note", "dentist"), ("Evidence", "dentist on Monday")), test.Context));
        Assert.Null(guard.Check(Tool<RecordMoodCommand>(("Note", "gym"), ("Evidence", "gym tonight")), test.Context));
        Assert.Contains($"at most {ToolGuard.MaxChangesPerStep}", guard.Check(Tool<RecordMoodCommand>(("Note", "call"), ("Evidence", "call with Rii")), test.Context));

        guard.BeginStep();
        Assert.Null(guard.Check(Tool<RecordMoodCommand>(("Note", "call"), ("Evidence", "call with Rii")), test.Context));
    }

    [Fact]
    public void Offered_LeavesOutStateToolsWhenSheReachesOutOnHerOwn()
    {
        Command[] tools = [Tool<RecordMoodCommand>(), Tool<ChangeOutfitCommand>(), Tool<ListRoutinesCommand>(), Tool<SyncHealthCommand>()];

        Assert.Equal(["ListRoutines", "SyncHealth"], ToolGuard.Offered(tools, TurnSource.System).Select(t => t.Name));
        Assert.Equal(4, ToolGuard.Offered(tools, TurnSource.User).Length);
    }

    [Fact]
    public void FactTools_AskForEvidence()
    {
        Command[] facts = [new RecordMoodCommand(), new ScheduleCheckInCommand(), new RememberPersonCommand(), new StartWorkCommand(),
                           new LogSleepCommand(), new TakeBreakCommand(), new RecordJapaneseStrengthCommand(), new RemindCommand()];

        foreach (Command command in facts)
        {
            Assert.IsAssignableFrom<IUserFactCommand>(command);
            Argument evidence = Assert.Single(command.GetDefaultArguments(), a => a.Name == Grounding.EvidenceName);
            Assert.True(evidence.IsRequired);
        }
        Assert.IsNotAssignableFrom<IStateCommand>(new ListRoutinesCommand());
        Assert.IsNotAssignableFrom<IStateCommand>(new SyncHealthCommand());
    }

    [Fact]
    public async Task Puppeteer_OnProactiveTurn_IsNotOfferedAndCannotRunRecordingTools()
    {
        ScriptedLLM llm = new(context =>
        [
            Tool<RecordMoodCommand>(("Note", "officially moving to Japan"), ("Kind", "victory"), ("Evidence", "moving to Japan")),
            Text("Good morning, baby!")
        ]);
        (LifeTestContext test, SinglePuppeteer puppeteer) = await Puppeteer(llm, TurnSource.System);
        test.Conversation.AddMessage(new TextMessage { CharacterId = null, From = Message.Type.System, Time = DateTime.UtcNow, Text = "[System] [Proactive] Morning greeting" });

        await puppeteer.ProcessAsync();

        Assert.DoesNotContain("RecordMood", llm.OfferedTools[0]);
        Assert.Contains("ListRoutines", llm.OfferedTools[0]);
        Assert.Empty(test.Profiles.Items.SelectMany(p => p.Mood));
        CommandMessage refused = Assert.Single(test.Conversation.Messages.OfType<CommandMessage>());
        Assert.StartsWith(ToolGuard.RefusalPrefix, refused.Output);
        Assert.Contains(test.Conversation.Messages.OfType<TextMessage>(), m => m.Text == "Good morning, baby!");
    }

    [Fact]
    public async Task Puppeteer_OnUserTurn_RecordsQuotedFactsAndReturnsToolErrorsToTheModel()
    {
        ScriptedLLM llm = new(context => context.Conversation.Messages.OfType<CommandMessage>().Any()
            ? [Text("Rest a bit, okay?")]
            :
            [
                Tool<RecordMoodCommand>(("Note", "exhausted after work"), ("Evidence", "so tired after work")),
                Tool<RecordMoodCommand>(("Note", "driving exam"), ("Kind", "worry"), ("Evidence", "driving exam")),
                Tool<LogSleepCommand>(("Hours", "lots"), ("Evidence", "slept badly"))
            ]);
        (LifeTestContext test, SinglePuppeteer puppeteer) = await Puppeteer(llm, TurnSource.User);
        test.Conversation.AddMessage(UserSays("I'm so tired after work and slept badly"));

        await puppeteer.ProcessAsync();

        MoodEntry mood = Assert.Single(test.Profile.Mood);
        Assert.Equal("exhausted after work", mood.Note);
        string[] outputs = [.. test.Conversation.Messages.OfType<CommandMessage>().Select(m => m.Output)];
        Assert.Contains(outputs, o => o.Contains("'driving exam' is not in any of his recent messages"));
        Assert.Contains(outputs, o => o.StartsWith("Error: Hours must be a number"));
        Assert.Contains(test.Conversation.Messages.OfType<TextMessage>(), m => m.Text == "Rest a bit, okay?");
    }

    [Fact]
    public async Task RecordMood_SkipsNearDuplicatesAndOpenConcerns()
    {
        LifeTestContext test = new();

        await test.Run<RecordMoodCommand>(("Note", "tired after day 4 back at work, wants to lie down"));
        string again = await test.Run<RecordMoodCommand>(("Note", "tired after day 4 back at work - wants to lie down"));
        await test.Run<RecordMoodCommand>(("Kind", "worry"), ("Note", "worried about the Dec 4 driving exam"), ("FollowUpAt", "2099-12-04"));
        string concern = await test.Run<RecordMoodCommand>(("Kind", "worry"), ("Note", "Worried about the Dec 4 driving exam!"), ("FollowUpAt", "2099-12-04"));

        Assert.StartsWith("Already recorded", again);
        Assert.StartsWith("Already tracking concern", concern);
        Assert.Equal(2, test.Profile.Mood.Count);
        Assert.Single(test.Profile.Concerns);
    }

    [Fact]
    public async Task ScheduleCheckIn_SkipsTheSameCheckInAgain()
    {
        LifeTestContext test = new();
        await test.Run<SetTimeZoneCommand>(("TimeZone", "Europe/Berlin"));

        string day = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

        await test.Run<ScheduleCheckInCommand>(("About", "Wish him luck before his driving exam"), ("At", $"{day} 09:00"));
        string sameTime = await test.Run<ScheduleCheckInCommand>(("About", "Ask about the new game"), ("At", $"{day} 09:15"));
        string sameNote = await test.Run<ScheduleCheckInCommand>(("About", "Wish him luck before his driving exam today"), ("At", $"{day} 07:00"));
        string other = await test.Run<ScheduleCheckInCommand>(("About", "Ask how the exam went"), ("At", $"{day} 18:00"));

        Assert.StartsWith("You already have", sameTime);
        Assert.StartsWith("You already have", sameNote);
        Assert.StartsWith("Scheduled", other);
        Assert.Equal(2, test.Routines.Items.Count());
    }

    [Fact]
    public async Task RememberPerson_DoesNotStoreTheSameNoteReworded()
    {
        LifeTestContext test = new();

        await test.Run<RememberPersonCommand>(("Name", "Rii"), ("Note", "Wants to understand English to watch movies without subtitles"));
        await test.Run<RememberPersonCommand>(("Name", "Rii"), ("Note", "Wants to understand English so she can watch movies without subtitles."));
        await test.Run<RememberPersonCommand>(("Name", "Rii"), ("Note", "Likes onsen"));

        Assert.Equal(2, test.Companions.Items.Single().FindPerson("Rii")!.Notes.Count);
    }

    [Fact]
    public void WorkLog_ClosesSessionsNobodyHeardOfFor12Hours()
    {
        DateTime start = new(2026, 11, 4, 7, 0, 0, DateTimeKind.Utc);
        WorkLog log = new();
        log.Start("Portfolio site", string.Empty, start);

        Assert.Null(log.CloseIfIdle(start.AddHours(11), null));
        Assert.Null(log.CloseIfIdle(start.AddHours(13), start.AddHours(2)));

        WorkSession closed = log.CloseIfIdle(start.AddHours(26), start.AddHours(2))!;
        Assert.Equal(start.AddHours(2), closed.EndUtc);
        Assert.Contains("Closed automatically", closed.Notes);
        Assert.Null(log.Active);
    }

    [Fact]
    public void GroupView_PersonaSeesOnlyItsOwnSpeechAndTools()
    {
        Context context = ViewContext("akagi", "Akagi", MemberSpeech.Hide);
        Message[] view = GroupView.For(GroupConversation(), context);

        Assert.DoesNotContain(view, m => m is TextMessage t && t.Text.Contains("What are you about to work on?"));
        Assert.DoesNotContain(view, m => m is TextMessage t && t.Text.Contains("[Consultation for"));
        Assert.DoesNotContain(view, m => m is CommandMessage c && c.CharacterId == "kai");
        Assert.Contains(view, m => m is CommandMessage c && c.CharacterId == "akagi");
        Assert.Contains(view, m => m is TextMessage t && t.Text == "Hey baby");
        Assert.Contains(view, m => m is TextMessage t && t.Text == "How was your day?");
    }

    [Fact]
    public void GroupView_AdvisorSeesLabeledSpeechAndOnlyItsOwnConsultation()
    {
        Context context = ViewContext("kai", "Kai", MemberSpeech.Label);
        context.MemberNames = new Dictionary<string, string> { ["akagi"] = "Akagi", ["kai"] = "Kai", ["hana"] = "Hana" };
        Message[] view = GroupView.For(GroupConversation(), context);

        TextMessage persona = Assert.Single(view.OfType<TextMessage>(), t => t.Text.Contains("Hey baby"));
        Assert.Equal("[Akagi] Hey baby", persona.Text);
        Assert.Equal(Message.Type.System, persona.From);
        Assert.Contains(view, m => m is TextMessage t && t.Text.Contains("[Consultation for Kai from Akagi]"));
        Assert.DoesNotContain(view, m => m is TextMessage t && t.Text.Contains("[Consultation for Hana from Akagi]"));
        Assert.DoesNotContain(view, m => m is CommandMessage c && c.CharacterId == "akagi");
    }

    [Fact]
    public void LatestTurnStartedByUser_IgnoresConsultationsButNotRoutines()
    {
        DateTime now = DateTime.UtcNow;
        List<Message> messages =
        [
            UserSays("hi"),
            new TextMessage { CharacterId = null, From = Message.Type.System, Time = now, Text = "[System] [Consultation for Kai from Akagi] ..." },
            new TextMessage { From = Message.Type.Character, CharacterId = "akagi", Time = now, Text = "hey" }
        ];
        Assert.True(Reflector.LatestTurnStartedByUser(messages));

        messages.Add(new TextMessage { CharacterId = null, From = Message.Type.System, Time = now, Text = "[System] [Proactive] Morning greeting" });
        Assert.False(Reflector.LatestTurnStartedByUser(messages));
        Assert.False(Reflector.LatestTurnStartedByUser([]));
    }

    [Fact]
    public void Reflectors_SkipWithoutUserMessagesExceptMemoryConsolidation()
    {
        Assert.True(new SingleReflector().OnlyAfterUserMessages);
        Assert.False(new MemoryConsolidationReflector().OnlyAfterUserMessages);
    }

    [Fact]
    public void Argument_KeepsItsValueThroughTheDatabase()
    {
        foreach ((Argument.Type type, string value) in new[] { (Argument.Type.String, "nervous about the exam"), (Argument.Type.Int, "7"), (Argument.Type.Float, "6.5"), (Argument.Type.Bool, "True") })
        {
            Argument argument = new() { Name = "X", Description = "d", ArgumentType = type, Value = value };

            BsonDocument document = argument.ToBsonDocument();
            Argument loaded = BsonSerializer.Deserialize<Argument>(document);

            Assert.False(document.Contains("IntValue"));
            Assert.Equal(value, loaded.Value);
        }
    }

    [Fact]
    public void Argument_LoadsDocumentsStoredWithTheOldTypedFields()
    {
        BsonDocument old = new()
        {
            { "Name", "Note" }, { "Description", "d" }, { "IsRequired", true }, { "ArgumentType", 0 }, { "Value", "nervous" },
            { "IntValue", BsonNull.Value }, { "FloatValue", BsonNull.Value }, { "BoolValue", BsonNull.Value }
        };

        Assert.Equal("nervous", BsonSerializer.Deserialize<Argument>(old).Value);
    }

    [Fact]
    public void ProactiveInstruction_ForbidsGuessingAboutHisDay()
    {
        string instruction = RoutineEngine.BuildInstruction(new DailyRoutine { Name = "Morning greeting" }, RoutineDecision.Yes("daily at 08:00"));

        Assert.Contains("Only mention things he actually told you", instruction);
    }

    private static TextMessage UserSays(string text) => new() { CharacterId = null, From = Message.Type.User, Time = DateTime.UtcNow, Text = text };

    private static T Tool<T>(params (string Name, string Value)[] values) where T : Command, new()
    {
        T command = new();
        Argument[] arguments = command.GetDefaultArguments();
        foreach ((string name, string value) in values)
        {
            arguments.Single(a => a.Name == name).Value = value;
        }
        command.Arguments = arguments;
        command.SetFrom(Message.Type.Character, "akagi");
        return command;
    }

    private static TextMessageCommand Text(string text)
    {
        TextMessageCommand command = new();
        command.SetMessage(text);
        command.SetFrom(Message.Type.Character, "akagi");
        return command;
    }

    private static async Task<(LifeTestContext Test, SinglePuppeteer Puppeteer)> Puppeteer(ScriptedLLM llm, TurnSource turn)
    {
        LifeTestContext test = new();
        test.Context.Turn = turn;
        test.Context.LLMFactory = new ScriptedLLMFactory(llm);

        SystemProcessor processor = new()
        {
            Name = "Akagi",
            RunMode = ILLM.RunMode.Mixed,
            CommandNames = ["RecordMood", "LogSleep", "ListRoutines"]
        };
        processor.Commands = [new RecordMoodCommand(), new LogSleepCommand(), new ListRoutinesCommand()];
        ProcessorDatabase processors = new();
        await processors.SaveDocumentAsync(processor);

        SinglePuppeteer puppeteer = new() { Name = "Akagi Puppeteer", MaxSteps = 3, SystemProcessorId = processor.Id! };
        await puppeteer.Init(NullLogger.Instance, test.Context, processors);
        return (test, puppeteer);
    }

    private static Context ViewContext(string id, string name, MemberSpeech mode) => new()
    {
        Character = new Character { Id = id, Name = name },
        Conversation = new Conversation(),
        User = new User { Name = "Rhyth" },
        Communicator = new RecordingCommunicator(),
        LLMFactory = new NoLLMFactory(),
        DatabaseFactory = new InMemoryDatabaseFactory(),
        OtherMembers = mode
    };

    private static List<Message> GroupConversation()
    {
        DateTime now = DateTime.UtcNow;
        return
        [
            new TextMessage { CharacterId = null, From = Message.Type.User, Time = now, Text = "How was your day?" },
            new TextMessage { From = Message.Type.Character, CharacterId = "akagi", Time = now, Text = "Hey baby" },
            new CommandMessage { From = Message.Type.Character, CharacterId = "akagi", Time = now, Command = new StopExecutionCommand(), Output = "Kai answered: ..." },
            new TextMessage { CharacterId = null, From = Message.Type.System, Time = now, Text = "[System] [Consultation for Kai from Akagi] Is he overworking?" },
            new CommandMessage { From = Message.Type.Character, CharacterId = "kai", Time = now, Command = new WorkStatusCommand(), Output = "Working for 3h" },
            new TextMessage { From = Message.Type.Character, CharacterId = "kai", Time = now, Text = "Hey Rhyth. What are you about to work on?" },
            new TextMessage { CharacterId = null, From = Message.Type.System, Time = now, Text = "[System] [Consultation for Hana from Akagi] How did he sleep?" },
        ];
    }

    private sealed class ProcessorDatabase : InMemoryDatabase<SystemProcessor>, ISystemProcessorDatabase
    {
        public ProcessorDatabase() : base("system_processors")
        {
        }

        public async Task<SystemProcessor> GetSystemProcessor(string id) => (await GetDocumentByIdAsync(id))!;

        public Task<SystemProcessor[]> GetSystemProcessor(string[] ids) => throw new NotSupportedException();
    }

    private sealed class ScriptedLLMFactory(ILLM llm) : ILLMFactory
    {
        public Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType) => Task.FromResult(llm);
    }

    /// <summary>
    /// Answers each step from a script and remembers which tools each request offered.
    /// </summary>
    private sealed class ScriptedLLM(Func<Context, Command[]> script) : ILLM
    {
        public List<string[]> OfferedTools { get; } = [];

        public void SetModel(string model)
        {
        }

        public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context)
        {
            OfferedTools.Add([.. systemProcessor.CurrentCommands.Select(c => c.Name)]);
            return Task.FromResult(script(context));
        }

        public async IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(SystemProcessor systemProcessor, Context context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new StreamCompleteEvent(await GetNextSteps(systemProcessor, context), string.Empty);
        }
    }
}
