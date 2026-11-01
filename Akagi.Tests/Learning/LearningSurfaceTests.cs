using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Chat.Transmissions.Requests;
using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Characters.CharacterBehaviors.MessageCompilers.Injections;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Communication.Commands;
using Akagi.Communication.Commands.Life;
using Akagi.Communication.SocketComs.Transmissions;
using Akagi.Data;
using Akagi.Graphs;
using Akagi.Learning;
using Akagi.LLMs;
using Akagi.Tests.Support;
using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;

namespace Akagi.Tests.Learning;

public class LearningSocketTests
{
    private readonly LearningFixture _fixture = new();
    private readonly LearningRequestHandler _handler;

    public LearningSocketTests()
    {
        _handler = new LearningRequestHandler(_fixture.Service, NullLogger<LearningRequestHandler>.Instance);
    }

    private Task<LearningResponseTransmission> Send(LearningAction action, Action<LearningRequestTransmission>? set = null)
    {
        LearningRequestTransmission request = new() { RequestId = "r1", Action = action };
        set?.Invoke(request);
        return _handler.ProcessAsync(_fixture.UserId, request);
    }

    [Fact]
    public async Task AnswersStayHidden_UntilTheQuestionIsAnswered()
    {
        LearningResponseTransmission started = await Send(LearningAction.Start, r =>
        {
            r.Topic = "Rust";
            r.QuestionCount = 2;
        });

        Assert.Equal("r1", started.RequestId);
        LearningQuestion hidden = started.Lesson!.Questions[0];
        Assert.Null(hidden.CorrectOption);
        Assert.Equal("", hidden.ExpectedAnswer);
        Assert.Equal("", hidden.Feedback);
        Assert.Equal(4, hidden.Options.Length);
        Assert.Equal(0, started.Lesson.NextIndex);

        LearningResponseTransmission answered = await Send(LearningAction.Answer, r =>
        {
            r.LessonId = started.Lesson.Id;
            r.ChosenOption = 1;
        });

        LearningQuestion shown = answered.Lesson!.Questions[0];
        Assert.Equal(LessonVerdict.Wrong, shown.Verdict);
        Assert.Equal(0, shown.CorrectOption);
        Assert.Equal("answer 0", shown.ExpectedAnswer);
        Assert.Equal("Because 0.", shown.Feedback);
        Assert.Null(answered.Lesson.Questions[1].CorrectOption);
        Assert.Equal(1, answered.Lesson.NextIndex);
    }

    [Fact]
    public async Task Overview_NamesTheTutorAndWeakSpots_AndFinishReturnsTheResult()
    {
        LearningResponseTransmission started = await Send(LearningAction.Start, r => r.Topic = "Rust");
        await Send(LearningAction.Answer, r =>
        {
            r.LessonId = started.Lesson!.Id;
            r.ChosenOption = 1;
        });

        LearningLesson finished = (await Send(LearningAction.Finish, r => r.LessonId = started.Lesson!.Id)).Lesson!;
        LearningOverview overview = (await Send(LearningAction.Overview)).Overview!;

        Assert.Equal(LessonStatus.Finished, finished.Status);
        Assert.Equal("Well done.", finished.Closing);
        Assert.Equal("Mio", overview.Tutor.Name);
        Assert.True(overview.Tutor.IsTutor);
        Assert.Null(overview.Current);
        Assert.Single(overview.Recent);
        Assert.Equal("concept 0", Assert.Single(overview.Struggles).Title);
    }

    [Fact]
    public async Task ReadableErrorsReachTheUser_OthersStayGeneric()
    {
        LearningResponseTransmission refused = await Send(LearningAction.Finish, r => r.LessonId = "missing");
        Assert.Equal("That lesson could not be found.", refused.Error);

        LearningResponseTransmission unknownUser = await _handler.ProcessAsync("nobody", new LearningRequestTransmission { Action = LearningAction.Overview });
        Assert.Equal("Your account could not be found.", unknownUser.Error);

        LearningRequestHandler broken = new(null!, NullLogger<LearningRequestHandler>.Instance);
        LearningResponseTransmission crashed = await broken.ProcessAsync("x", new LearningRequestTransmission());
        Assert.Equal("Something went wrong. Try again in a moment.", crashed.Error);
    }

    [Fact]
    public void Transmissions_SurviveTheWire()
    {
        LearningResponseTransmission response = new()
        {
            RequestId = "r9",
            Overview = new LearningOverview
            {
                Tutor = new Bridge.Chat.Models.Character { Name = "Mio", IsTutor = true },
                Struggles = [new LearningStruggle { Id = "jp:a", Kind = StruggleKind.Japanese, Title = "に" }],
                Current = new LearningLesson
                {
                    Mode = LessonMode.Typed,
                    Questions = [new LearningQuestion { Prompt = "Q", Verdict = LessonVerdict.Partial, ChosenOption = 2 }]
                }
            }
        };

        LearningResponseTransmission read = MessagePackSerializer.Deserialize<LearningResponseTransmission>(MessagePackSerializer.Serialize(response));

        Assert.True(read.Overview!.Tutor.IsTutor);
        Assert.Equal(StruggleKind.Japanese, read.Overview.Struggles[0].Kind);
        Assert.Equal(2, read.Overview.Current!.Questions[0].ChosenOption);
        Assert.Equal(LessonVerdict.Partial, read.Overview.Current.Questions[0].Verdict);
        Assert.Equal(1, read.Overview.Current.AnsweredCount);
    }
}

public class LearnCommandTests
{
    private readonly LearningFixture _fixture = new();
    private readonly RecordingCommunicator _communicator = new();
    private readonly LearnCommand _command;

    public LearnCommandTests()
    {
        _command = new LearnCommand(_fixture.Service);
        _command.Init(_communicator);
    }

    private Task<CommandResult> Run(params string[] args) =>
        _command.ExecuteAsync(new Akagi.Communication.Commands.Command.Context { User = _fixture.User, DatabaseFactory = _fixture.Database }, args);

    [Fact]
    public async Task ChoiceLesson_RunsToTheEnd()
    {
        Assert.True((await Run("start", "Rust", "lifetimes")).Success);

        Lesson lesson = _fixture.Lessons.Items.Single();
        Assert.Equal("Rust lifetimes", lesson.Topic);
        Assert.Equal(LessonMode.MultipleChoice, lesson.Mode);
        Assert.Contains("Mio is preparing", _communicator.Sent[0]);
        Assert.Contains("Mio: Let's go.", _communicator.Sent[1]);
        Assert.Contains("A) answer 0", _communicator.Sent[1]);

        for (int i = 0; i < 4; i++)
        {
            await Run("answer", "a");
        }
        await Run("answer", "B");

        Assert.Equal(LessonStatus.Finished, lesson.Status);
        Assert.Contains("Not quite. Answer: answer 4", _communicator.Sent[^1]);
        Assert.Contains("Lesson finished: 4 of 5.", _communicator.Sent[^1]);
        Assert.Contains("Mio: Well done.", _communicator.Sent[^1]);
    }

    [Fact]
    public async Task TypedLesson_TakesTheWholeAnswer()
    {
        await Run("start", "typed", "Rust");

        await Run("answer", "the", "whole", "sentence");

        Lesson lesson = _fixture.Lessons.Items.Single();
        Assert.Equal(LessonMode.Typed, lesson.Mode);
        Assert.Equal("Rust", lesson.Topic);
        Assert.Equal("the whole sentence", lesson.Questions[0].AnswerText);
        Assert.Contains("Partly right. Answer: answer 0", _communicator.Sent[^1]);
    }

    [Fact]
    public async Task InvalidChoice_IsExplainedAndTheQuestionStaysOpen()
    {
        await Run("start", "Rust");

        CommandResult result = await Run("answer", "E");

        Assert.False(result.Success);
        Assert.Equal("Pick one of the four answers.", _communicator.Sent[^1]);
        Assert.False(_fixture.Lessons.Items.Single().Questions[0].IsAnswered);
    }

    [Fact]
    public async Task Status_AndStop()
    {
        await Run();
        Assert.Contains("Your tutor is Mio.", _communicator.Sent[^1]);

        await Run("stop");
        Assert.Equal("No lesson is running.", _communicator.Sent[^1]);

        await Run("start", "Rust");
        await Run("status");
        Assert.Contains("Question 1 of 5", _communicator.Sent[^1]);

        await Run("stop");
        Assert.Equal("Lesson stopped.", _communicator.Sent[^1]);
    }

    [Fact]
    public void Palette_OffersEveryForm()
    {
        Bridge.Chat.Models.Command described = _command.Describe();

        Assert.Equal("Life", described.Category);
        Assert.Equal(4, described.Forms.Length);
    }

    [Theory]
    [InlineData("a", 0)]
    [InlineData(" D ", 3)]
    [InlineData("2", 1)]
    [InlineData("E", null)]
    [InlineData("ab", null)]
    public void ParseOption_ReadsLettersAndNumbers(string text, int? expected)
    {
        Assert.Equal(expected, LearnCommand.ParseOption(text));
    }
}

public class TutorTests
{
    private const string UserId = "65f000000000000000000042";

    [Fact]
    public void Mio_IsAValidGraphWithoutWarnings_AndTaggedAsTutor()
    {
        GraphData graph = GraphData.Parse(TutorProvider.ReadDefaultGraph());

        Assert.Contains(graph.Nodes, n => n.TypeName.Contains("LearningInjectionCompiler"));
    }

    [Fact]
    public async Task Mio_PassesTheGraphValidator()
    {
        GraphValidationReport report = await new GraphValidator().ValidateAsync(GraphData.Parse(TutorProvider.ReadDefaultGraph()), null);

        Assert.True(report.IsValid && report.WarningCount == 0, report.ToText());
    }

    [Fact]
    public async Task Mio_ChatsOnTheLearningLLM_WithoutTools()
    {
        InMemoryDatabaseFactory database = new();
        GraphLoader loader = new(database);
        loader.LoadGraph(GraphData.Parse(TutorProvider.ReadDefaultGraph()));

        List<Savable> created = await loader.Create(UserId, TutorProvider.DefaultGraphName);

        Akagi.Characters.Character mio = Assert.Single(created.OfType<Akagi.Characters.Character>());
        Assert.Equal("Mio", mio.Name);
        Assert.Equal(UserId, mio.UserId);

        SystemProcessor processor = Assert.Single(created.OfType<SystemProcessor>());
        Assert.Equal(ILLM.LLMUsage.Learning, processor.Usage);
        // Gemini 3 needs reasoning details to continue a tool exchange, so a tutor on it must not use tools
        Assert.Equal(ILLM.RunMode.TextOnly, processor.RunMode);
        Assert.Empty(processor.CommandNames);
        Assert.Contains("{{description}}", processor.SystemInstruction);

        Assert.Contains(created, s => s is LearningInjectionCompiler);
        Assert.Contains(created, s => s is JapaneseDiagnosisInjectionCompiler);
        Akagi.Characters.Cards.Card card = Assert.Single(created.OfType<Akagi.Characters.Cards.Card>());
        Assert.Contains(TutorProvider.Tag, card.Tags);
    }

    [Fact]
    public void Tutor_IsRecognizedByItsCardTag()
    {
        Akagi.Characters.Character tutor = LearningFixture.CreateTutor();
        Akagi.Characters.Character other = new();
        other.Init(new Akagi.Characters.Cards.Card { Tags = ["companion"] });

        Assert.True(TutorProvider.IsTutor(tutor));
        Assert.False(TutorProvider.IsTutor(other));
    }

    [Fact]
    public async Task Provider_UsesAnExistingTutor()
    {
        LearningFixture fixture = new();
        TutorProvider provider = new(fixture.Database, fixture.Characters, NullLogger<TutorProvider>.Instance);
        fixture.Tutor.UserId = fixture.UserId;

        Assert.Same(fixture.Tutor, await provider.GetAsync(fixture.User));
        Assert.Empty(fixture.Database.Documents);
    }

    [Fact]
    public void Injection_ShowsTheLatestLessonAndWeakSpots()
    {
        DateTime now = DateTime.UtcNow;
        Lesson lesson = new()
        {
            Status = LessonStatus.Finished,
            Topic = "Particles",
            CreatedAt = now.AddHours(-2),
            Questions =
            [
                new LessonQuestion { Prompt = "Pick one", Options = ["に", "で"], ChosenOption = 0, Answer = "で", Verdict = LessonVerdict.Wrong },
                new LessonQuestion { Prompt = "Unanswered" }
            ]
        };

        string text = LearningInjectionCompiler.Render([lesson], [new Struggle("x", StruggleKind.Lesson, "Traits", "Missed 2x", 1)], now, 3);

        Assert.Contains("Latest lesson: Particles (0/1,", text);
        Assert.Contains("- Wrong: Pick one They answered: に. Correct: で", text);
        Assert.DoesNotContain("Unanswered", text);
        Assert.Contains("- Traits: Missed 2x", text);
        Assert.Equal("", LearningInjectionCompiler.Render([], [], now, 3));
    }
}
