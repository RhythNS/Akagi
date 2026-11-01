using Akagi.Bridge.Chat.Models;
using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Learning;

internal class LessonQuestion
{
    /// <summary>
    /// What the question tests, in a few words. Misses are tracked per concept.
    /// </summary>
    public string Concept { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    /// <summary>
    /// The four answers in display order; empty for typed lessons.
    /// </summary>
    public string[] Options { get; set; } = [];
    public int CorrectOption { get; set; } = -1;
    /// <summary>
    /// The correct option, or a model answer for typed lessons.
    /// </summary>
    public string Answer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    /// <summary>
    /// The <see cref="Struggle.Id"/> this question works on, if any.
    /// </summary>
    public string FocusId { get; set; } = string.Empty;

    public LessonVerdict Verdict { get; set; } = LessonVerdict.Unanswered;
    public int? ChosenOption { get; set; }
    public string AnswerText { get; set; } = string.Empty;
    public string Feedback { get; set; } = string.Empty;
    public DateTime? AnsweredAt { get; set; }

    [BsonIgnore]
    public bool IsAnswered => Verdict != LessonVerdict.Unanswered;
}

/// <summary>
/// One sitting with the tutor: a set of questions on the user's weak spots or a custom topic.
/// </summary>
internal class Lesson : Savable
{
    private string _userId = string.Empty;
    private string _characterId = string.Empty;
    private LessonMode _mode;
    private LessonStatus _status = LessonStatus.Preparing;
    private string _customTopic = string.Empty;
    private string _topic = string.Empty;
    private string _intro = string.Empty;
    private string _closing = string.Empty;
    private string _error = string.Empty;
    private DateTime _createdAt = DateTime.UtcNow;
    private DateTime? _finishedAt;

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }
    public LessonMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }
    public LessonStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
    /// <summary>
    /// What the user asked for; empty when the lesson works on their weak spots.
    /// </summary>
    public string CustomTopic
    {
        get => _customTopic;
        set => SetProperty(ref _customTopic, value);
    }
    /// <summary>
    /// The title shown to the user.
    /// </summary>
    public string Topic
    {
        get => _topic;
        set => SetProperty(ref _topic, value);
    }
    public string Intro
    {
        get => _intro;
        set => SetProperty(ref _intro, value);
    }
    public string Closing
    {
        get => _closing;
        set => SetProperty(ref _closing, value);
    }
    public string Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }
    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }
    public DateTime? FinishedAt
    {
        get => _finishedAt;
        set => SetProperty(ref _finishedAt, value);
    }
    public List<LessonQuestion> Questions { get; set; } = [];

    [BsonIgnore]
    public int AnsweredCount => Questions.Count(q => q.IsAnswered);

    [BsonIgnore]
    public bool IsOpen => Status is LessonStatus.Preparing or LessonStatus.Active;

    /// <summary>
    /// One point for a correct answer, a half for a partly correct one.
    /// </summary>
    [BsonIgnore]
    public double Score => Questions.Sum(q => q.Verdict switch
    {
        LessonVerdict.Correct => 1,
        LessonVerdict.Partial => 0.5,
        _ => 0
    });

    [BsonIgnore]
    public LessonQuestion? NextQuestion => Questions.FirstOrDefault(q => !q.IsAnswered);
}

internal interface ILessonDatabase : IDatabase<Lesson>
{
    /// <summary>
    /// The user's lessons, newest first.
    /// </summary>
    public Task<List<Lesson>> GetForUser(string userId);
}

internal class LessonDatabase : Database<Lesson>, ILessonDatabase
{
    public override string CollectionName => "lessons";

    public LessonDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Lesson;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Lesson)savable);

    public async Task<List<Lesson>> GetForUser(string userId)
    {
        List<Lesson> lessons = await GetDocumentsByPredicateAsync(Builders<Lesson>.Filter.Eq(l => l.UserId, userId));
        return [.. lessons.OrderByDescending(l => l.CreatedAt)];
    }
}
