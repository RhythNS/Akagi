using Akagi.Bridge.Chat.Models;
using Akagi.Characters;
using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Learning;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Life;
using Akagi.Tests.Support;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Character = Akagi.Characters.Character;
using Command = Akagi.Receivers.Commands.Command;

namespace Akagi.Tests.Learning;

internal class InMemoryLessonDatabase : InMemoryDatabase<Lesson>, ILessonDatabase
{
    public InMemoryLessonDatabase() : base("lessons")
    {
    }

    public Task<List<Lesson>> GetForUser(string userId) =>
        Task.FromResult(Items.Where(l => l.UserId == userId).OrderByDescending(l => l.CreatedAt).ToList());
}

internal class InMemoryMessageCompilerDatabase : InMemoryDatabase<MessageCompiler>, IMessageCompilerDatabase
{
    public InMemoryMessageCompilerDatabase() : base("message_compilers")
    {
    }
}

internal class FixedTutorProvider : ITutorProvider
{
    public required Character Tutor { get; init; }

    public Task<Character> GetAsync(User user) => Task.FromResult(Tutor);
}

/// <summary>
/// An author with canned questions, for testing the lesson flow without an LLM.
/// </summary>
internal class FakeLessonAuthor : ILessonAuthor
{
    public int GradeCalls { get; private set; }
    public LessonBrief? LastBrief { get; private set; }
    public LessonGrade Grade { get; set; } = new(LessonVerdict.Partial, "Close.");
    public Exception? WriteFailure { get; set; }
    public Exception? GradeFailure { get; set; }
    public Exception? CloseFailure { get; set; }

    public Task<LessonDraft> WriteAsync(LessonBrief brief, User user, Character tutor)
    {
        LastBrief = brief;
        if (WriteFailure != null)
        {
            throw WriteFailure;
        }

        List<LessonQuestion> questions = [];
        for (int i = 0; i < brief.QuestionCount; i++)
        {
            questions.Add(new LessonQuestion
            {
                Concept = $"concept {i}",
                Prompt = $"Question {i}?",
                Answer = $"answer {i}",
                Explanation = $"Because {i}.",
                Options = brief.Mode == LessonMode.MultipleChoice ? [$"answer {i}", "b", "c", "d"] : [],
                CorrectOption = brief.Mode == LessonMode.MultipleChoice ? 0 : -1,
                FocusId = brief.Focus.Count > 0 ? brief.Focus[i % brief.Focus.Count].Id : string.Empty
            });
        }
        return Task.FromResult(new LessonDraft("Let's go.", questions));
    }

    public Task<LessonGrade> GradeAsync(Lesson lesson, LessonQuestion question, string answer, User user, Character tutor)
    {
        GradeCalls++;
        return GradeFailure != null ? Task.FromException<LessonGrade>(GradeFailure) : Task.FromResult(Grade);
    }

    public Task<string> CloseAsync(Lesson lesson, User user, Character tutor) =>
        CloseFailure != null ? Task.FromException<string>(CloseFailure) : Task.FromResult("Well done.");
}

/// <summary>
/// Plays back one response per request and records what each request saw.
/// </summary>
internal class ScriptedLearningLLM : ILLM, ILLMFactory, ICommandFactory
{
    private readonly Queue<Func<Command[]>> _responses = new();

    public List<string> Requests { get; } = [];
    public List<string> Instructions { get; } = [];
    public List<ILLM.LLMUsage> Usages { get; } = [];
    public List<ILLM.RunMode> RunModes { get; } = [];

    public void Respond(params Command[] commands) => _responses.Enqueue(() => commands);

    public void Fail(Exception exception) => _responses.Enqueue(() => throw exception);

    public static AddLessonQuestionCommand Question(string prompt, string answer = "right", string[]? wrong = null, string focusId = "")
    {
        wrong ??= ["w1", "w2", "w3"];
        AddLessonQuestionCommand command = new();
        Argument[] args = command.GetDefaultArguments();
        Set(args, "Concept", prompt + " concept");
        Set(args, "Question", prompt);
        Set(args, "Answer", answer);
        Set(args, "Explanation", "Because.");
        Set(args, "FocusId", focusId);
        for (int i = 0; i < wrong.Length; i++)
        {
            Set(args, $"Wrong{i + 1}", wrong[i]);
        }
        command.Arguments = args;
        return command;
    }

    public static SetLessonIntroCommand Intro(string text) => With(new SetLessonIntroCommand(), ("Intro", text));

    public static GradeLessonAnswerCommand Grade(string verdict, string feedback) =>
        With(new GradeLessonAnswerCommand(), ("Verdict", verdict), ("Feedback", feedback));

    public static CloseLessonCommand Close(string message) => With(new CloseLessonCommand(), ("Message", message));

    private static T With<T>(T command, params (string Name, string Value)[] values) where T : Command
    {
        Argument[] args = command.GetDefaultArguments();
        foreach ((string name, string value) in values)
        {
            Set(args, name, value);
        }
        command.Arguments = args;
        return command;
    }

    private static void Set(Argument[] args, string name, string value) => args.Single(a => a.Name == name).Value = value;

    public void SetModel(string model)
    {
    }

    public Task<ILLM> Create(User user, LLMDefinition? overrideModel, ILLM.LLMUsage usageType)
    {
        Usages.Add(usageType);
        return Task.FromResult<ILLM>(this);
    }

    public T Create<T>() where T : Command => throw new NotSupportedException();

    public Command Create(string commandName) => commandName switch
    {
        "AddLessonQuestion" => new AddLessonQuestionCommand(),
        "SetLessonIntro" => new SetLessonIntroCommand(),
        "GradeLessonAnswer" => new GradeLessonAnswerCommand(),
        "CloseLesson" => new CloseLessonCommand(),
        _ => throw new InvalidOperationException(commandName)
    };

    public Task<Command[]> GetNextSteps(SystemProcessor systemProcessor, Context context)
    {
        RunModes.Add(systemProcessor.RunMode);
        Instructions.Add(systemProcessor.CompileSystemPrompt(context.User, context.Character));
        Requests.Add(string.Join('\n', context.Conversation.Messages.OfType<Akagi.Characters.Conversations.TextMessage>().Select(m => m.Text)));
        return Task.FromResult(_responses.Dequeue()());
    }

    public IAsyncEnumerable<LLMStreamEvent> GetNextStepsStreamAsync(SystemProcessor systemProcessor, Context context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// The learning services on in-memory databases.
/// </summary>
internal class LearningFixture
{
    public InMemoryDatabaseFactory Database { get; } = new();
    public InMemoryLessonDatabase Lessons { get; } = new();
    public InMemoryUserDatabase Users { get; } = new();
    public InMemoryCharacterDatabase Characters { get; } = new();
    public InMemoryLifeProfileDatabase Profiles { get; } = new();
    public InMemoryJapaneseDiagnosisDatabase Diagnoses { get; } = new();
    public FakeLessonAuthor Author { get; } = new();
    public User User { get; } = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Tester" };
    public Character Tutor { get; } = CreateTutor();
    public LearningService Service { get; }

    public LearningFixture()
    {
        Database.Register(Lessons);
        Database.Register(Users);
        Database.Register(Characters);
        Database.Register(Profiles);
        Database.Register(Diagnoses);
        Database.Register(new InMemoryMessageCompilerDatabase());
        Users.SaveDocumentAsync(User);
        Characters.SaveDocumentAsync(Tutor);
        Service = new LearningService(Database, Characters, Author, new FixedTutorProvider { Tutor = Tutor }, NullLogger<LearningService>.Instance);
    }

    public string UserId => User.Id!;

    public static Character CreateTutor()
    {
        Character tutor = new() { Id = ObjectId.GenerateNewId().ToString(), Name = "Mio", CardId = ObjectId.GenerateNewId().ToString() };
        tutor.Init(new Card { Name = "Mio", Description = "A tutor for {{user}}.", Tags = [TutorProvider.Tag] });
        return tutor;
    }

    public LessonAuthor CreateAuthor(ScriptedLearningLLM llm, int seed = 7)
    {
        ServiceCollection services = new();
        services.AddSingleton<ILLMFactory>(llm);
        services.AddSingleton<ICommandFactory>(llm);
        return new LessonAuthor(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), Database,
            NullLogger<LessonAuthor>.Instance, new Random(seed));
    }

    public Task<Lesson> StartCustom(LessonMode mode = LessonMode.MultipleChoice, int count = 2) =>
        Service.StartAsync(UserId, new LessonRequest(mode, "Rust lifetimes", [], count));
}
