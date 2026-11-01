using Akagi.Bridge.Chat.Models;
using Akagi.Characters;
using Akagi.Data;
using Akagi.Life;
using Akagi.Life.Japanese;
using Akagi.Life.Study;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using Character = Akagi.Characters.Character;

namespace Akagi.Learning;

internal record LessonRequest(LessonMode Mode, string Topic, IReadOnlyCollection<string> FocusIds, int QuestionCount);

internal record LearningState(Character Tutor, List<Struggle> Struggles, Lesson? Current, List<Lesson> Recent);

internal interface ILearningService
{
    public Task<LearningState> GetStateAsync(string userId);
    public Task<Lesson> StartAsync(string userId, LessonRequest request);
    /// <summary>
    /// Answers with <paramref name="option"/> in four answer lessons and <paramref name="text"/> in typed ones.
    /// An answered question keeps its first answer.
    /// </summary>
    public Task<Lesson> AnswerAsync(string userId, string lessonId, int questionIndex, int? option, string? text);
    /// <summary>
    /// Ends the lesson, also when questions are left, and records what it showed.
    /// </summary>
    public Task<Lesson> FinishAsync(string userId, string lessonId);
}

internal class LearningService : ILearningService
{
    public const int DefaultQuestionCount = 5;
    public const int MaxQuestionCount = 10;
    public const int MaxTopicLength = 300;
    public const int MaxAnswerLength = 2000;
    public const int RecentLessons = 5;
    public const int MaxLessonMinutes = 60;
    /// <summary>
    /// A lesson still preparing after this long was lost, for instance to a restart.
    /// </summary>
    public static readonly TimeSpan PrepareTimeout = TimeSpan.FromMinutes(5);

    private readonly IDatabaseFactory _databases;
    private readonly ICharacterDatabase _characters;
    private readonly ILessonAuthor _author;
    private readonly ITutorProvider _tutors;
    private readonly ILogger<LearningService> _logger;

    public LearningService(IDatabaseFactory databases, ICharacterDatabase characters, ILessonAuthor author, ITutorProvider tutors, ILogger<LearningService> logger)
    {
        _databases = databases;
        _characters = characters;
        _author = author;
        _tutors = tutors;
        _logger = logger;
    }

    private ILessonDatabase Lessons => _databases.GetDatabase<ILessonDatabase>();

    public async Task<LearningState> GetStateAsync(string userId)
    {
        User user = await LoadUser(userId);
        Character tutor = await _tutors.GetAsync(user);
        List<Lesson> history = await Lessons.GetForUser(userId);
        DateTime now = DateTime.UtcNow;

        return new LearningState(
            tutor,
            await FindStruggles(userId, history, now),
            history.FirstOrDefault(l => IsRunning(l, now)),
            [.. history.Where(l => l.Status == LessonStatus.Finished).Take(RecentLessons)]);
    }

    public async Task<Lesson> StartAsync(string userId, LessonRequest request)
    {
        User user = await LoadUser(userId);
        Character tutor = await _tutors.GetAsync(user);
        List<Lesson> history = await Lessons.GetForUser(userId);
        DateTime now = DateTime.UtcNow;

        if (history.Any(l => l.Status == LessonStatus.Preparing && IsRunning(l, now)))
        {
            throw new LearningException($"{tutor.Name} is still preparing your lesson.");
        }

        string topic = request.Topic.Trim();
        if (topic.Length > MaxTopicLength)
        {
            throw new LearningException($"Please keep the topic under {MaxTopicLength} characters.");
        }

        int count = request.QuestionCount <= 0 ? DefaultQuestionCount : Math.Min(request.QuestionCount, MaxQuestionCount);
        List<Struggle> focus = topic.Length > 0
            ? []
            : StruggleFinder.Select(await FindStruggles(userId, history, now), request.FocusIds, count);
        if (topic.Length == 0 && focus.Count == 0)
        {
            throw new LearningException("There are no weak spots to work on yet. Pick a topic instead.");
        }

        foreach (Lesson open in history.Where(l => l.IsOpen))
        {
            await LeaveAsync(open, user, now);
        }

        Lesson lesson = new()
        {
            UserId = userId,
            CharacterId = tutor.Id!,
            Mode = request.Mode,
            CustomTopic = topic,
            Topic = topic.Length > 0 ? topic : string.Join(", ", focus.Select(s => s.Title)),
            CreatedAt = now
        };
        await Lessons.SaveDocumentAsync(lesson);

        try
        {
            LessonDraft draft = await _author.WriteAsync(new LessonBrief(request.Mode, topic, focus, count), user, tutor);
            lesson.Intro = draft.Intro;
            lesson.Questions = draft.Questions;
            lesson.Status = LessonStatus.Active;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preparing a lesson failed for user {UserId}", userId);
            lesson.Status = LessonStatus.Failed;
            lesson.Error = ex is LearningException ? ex.Message : $"{tutor.Name} could not prepare the lesson. Try again in a moment.";
            await Lessons.SaveDocumentAsync(lesson);
            throw new LearningException(lesson.Error, ex);
        }

        await Lessons.SaveDocumentAsync(lesson);
        return lesson;
    }

    public async Task<Lesson> AnswerAsync(string userId, string lessonId, int questionIndex, int? option, string? text)
    {
        Lesson lesson = await LoadLesson(userId, lessonId);
        if (lesson.Status != LessonStatus.Active)
        {
            throw new LearningException("This lesson is over.");
        }
        if (questionIndex < 0 || questionIndex >= lesson.Questions.Count)
        {
            throw new LearningException("That question is not part of this lesson.");
        }

        LessonQuestion question = lesson.Questions[questionIndex];
        if (question.IsAnswered)
        {
            return lesson;
        }

        if (lesson.Mode == LessonMode.MultipleChoice)
        {
            AnswerChoice(question, option);
        }
        else
        {
            await AnswerTyped(lesson, question, userId, text);
        }

        question.AnsweredAt = DateTime.UtcNow;
        await Lessons.SaveDocumentAsync(lesson);
        return lesson;
    }

    public async Task<Lesson> FinishAsync(string userId, string lessonId)
    {
        Lesson lesson = await LoadLesson(userId, lessonId);
        if (lesson.Status != LessonStatus.Active)
        {
            return lesson;
        }

        User user = await LoadUser(userId);
        await LeaveAsync(lesson, user, DateTime.UtcNow);
        if (lesson.Status == LessonStatus.Finished)
        {
            await AddClosingAsync(lesson, user);
        }
        return lesson;
    }

    private static void AnswerChoice(LessonQuestion question, int? option)
    {
        if (option is not int chosen || chosen < 0 || chosen >= question.Options.Length)
        {
            throw new LearningException("Pick one of the four answers.");
        }
        question.ChosenOption = chosen;
        question.Verdict = chosen == question.CorrectOption ? LessonVerdict.Correct : LessonVerdict.Wrong;
        question.Feedback = question.Explanation;
    }

    private async Task AnswerTyped(Lesson lesson, LessonQuestion question, string userId, string? text)
    {
        string answer = text?.Trim() ?? string.Empty;
        if (answer.Length == 0)
        {
            throw new LearningException("Type an answer first.");
        }
        if (answer.Length > MaxAnswerLength)
        {
            throw new LearningException($"Please keep the answer under {MaxAnswerLength} characters.");
        }

        // The model answer itself needs no grading
        if (LessonAuthor.Normalize(answer) == LessonAuthor.Normalize(question.Answer))
        {
            question.AnswerText = answer;
            question.Verdict = LessonVerdict.Correct;
            question.Feedback = question.Explanation;
            return;
        }

        User user = await LoadUser(userId);
        Character tutor = await LoadTutor(lesson, user);
        LessonGrade grade;
        try
        {
            grade = await _author.GradeAsync(lesson, question, answer, user, tutor);
        }
        catch (Exception ex) when (ex is not LearningException)
        {
            _logger.LogError(ex, "Grading failed in lesson {LessonId}", lesson.Id);
            throw new LearningException($"{tutor.Name} could not grade that answer. Send it again.", ex);
        }

        question.AnswerText = answer;
        question.Verdict = grade.Verdict;
        question.Feedback = grade.Feedback;
    }

    /// <summary>
    /// Ends an open lesson. Answers already given still count; a lesson without any leaves no trace.
    /// </summary>
    private async Task LeaveAsync(Lesson lesson, User user, DateTime now)
    {
        lesson.FinishedAt = now;
        lesson.Status = lesson.AnsweredCount > 0 ? LessonStatus.Finished : LessonStatus.Abandoned;
        await Lessons.SaveDocumentAsync(lesson);

        if (lesson.Status == LessonStatus.Finished)
        {
            await RecordProgressAsync(lesson, user, now);
        }
    }

    /// <summary>
    /// Feeds the results back, so the diagnosis follows lessons and the companion sees that the user studied.
    /// </summary>
    private async Task RecordProgressAsync(Lesson lesson, User user, DateTime now)
    {
        LifeProfile profile = await _databases.GetDatabase<ILifeProfileDatabase>().GetOrCreate(user.Id!);
        foreach (LessonQuestion question in lesson.Questions.Where(q => q.IsAnswered && q.FocusId.StartsWith(Struggle.VocabularyPrefix, StringComparison.Ordinal)))
        {
            if (question.Verdict != LessonVerdict.Partial)
            {
                profile.Study.FindVocab(question.FocusId[Struggle.VocabularyPrefix.Length..])?.Review(question.Verdict == LessonVerdict.Correct, now);
            }
        }

        StudyDay day = profile.Study.GetDay(LifeClock.DateKey(profile.ToLocal(now)));
        day.Reviews += lesson.AnsweredCount;
        day.Minutes += Math.Clamp((int)Math.Ceiling((now - lesson.CreatedAt).TotalMinutes), 1, MaxLessonMinutes);
        day.Activities.Add($"{StudySummary.LessonActivityPrefix}{lesson.Topic} ({lesson.Score:0.#}/{lesson.AnsweredCount})");
        await _databases.GetDatabase<ILifeProfileDatabase>().SaveDocumentAsync(profile);

        List<LessonQuestion> diagnosed = [.. lesson.Questions.Where(q => q.IsAnswered && q.FocusId.StartsWith(Struggle.JapanesePrefix, StringComparison.Ordinal))];
        if (diagnosed.Count == 0)
        {
            return;
        }

        IJapaneseDiagnosisDatabase diagnoses = _databases.GetDatabase<IJapaneseDiagnosisDatabase>();
        JapaneseDiagnosis diagnosis = await diagnoses.GetOrCreate(user.Id!);
        foreach (LessonQuestion question in diagnosed)
        {
            DiagnosisIssue? issue = diagnosis.FindIssue(question.FocusId[Struggle.JapanesePrefix.Length..]);
            if (question.Verdict == LessonVerdict.Correct)
            {
                issue?.RecordCorrect(now);
            }
            else if (question.Verdict == LessonVerdict.Wrong)
            {
                issue?.RecordMistake(null, now);
            }
        }
        await diagnoses.SaveDocumentAsync(diagnosis);
    }

    /// <summary>
    /// The closing words are a nicety: the results stand without them.
    /// </summary>
    private async Task AddClosingAsync(Lesson lesson, User user)
    {
        try
        {
            lesson.Closing = await _author.CloseAsync(lesson, user, await LoadTutor(lesson, user));
            await Lessons.SaveDocumentAsync(lesson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Closing words failed for lesson {LessonId}", lesson.Id);
        }
    }

    private async Task<List<Struggle>> FindStruggles(string userId, IEnumerable<Lesson> history, DateTime now)
    {
        JapaneseDiagnosis? diagnosis = await _databases.GetDatabase<IJapaneseDiagnosisDatabase>().GetForUser(userId);
        LifeProfile profile = await _databases.GetDatabase<ILifeProfileDatabase>().GetOrCreate(userId);
        return StruggleFinder.Find(diagnosis, profile, history, now);
    }

    private static bool IsRunning(Lesson lesson, DateTime now) =>
        lesson.Status == LessonStatus.Active
        || (lesson.Status == LessonStatus.Preparing && now - lesson.CreatedAt < PrepareTimeout);

    private async Task<User> LoadUser(string userId) =>
        await _databases.GetDatabase<IUserDatabase>().GetDocumentByIdAsync(userId)
            ?? throw new LearningException("Your account could not be found.");

    private async Task<Lesson> LoadLesson(string userId, string lessonId)
    {
        Lesson? lesson = await Lessons.GetDocumentByIdAsync(lessonId);
        return lesson != null && lesson.UserId == userId ? lesson : throw new LearningException("That lesson could not be found.");
    }

    private async Task<Character> LoadTutor(Lesson lesson, User user) =>
        await _characters.GetCharacter(lesson.CharacterId) ?? await _tutors.GetAsync(user);
}
