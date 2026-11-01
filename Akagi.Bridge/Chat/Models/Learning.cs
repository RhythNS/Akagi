using MessagePack;

namespace Akagi.Bridge.Chat.Models;

public enum LessonMode
{
    /// <summary>Pick one of four answers.</summary>
    MultipleChoice,
    /// <summary>Type the answer; the tutor grades it.</summary>
    Typed
}

public enum LessonStatus
{
    Preparing,
    Active,
    Finished,
    Abandoned,
    Failed
}

public enum LessonVerdict
{
    Unanswered,
    Correct,
    Partial,
    Wrong
}

public enum StruggleKind
{
    /// <summary>A focus area of the Japanese diagnosis.</summary>
    Japanese,
    /// <summary>A recurring grammar mistake.</summary>
    Grammar,
    /// <summary>A word that keeps slipping.</summary>
    Vocabulary,
    /// <summary>Something missed in an earlier lesson, on any topic.</summary>
    Lesson
}

[MessagePackObject]
public class LearningStruggle
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public StruggleKind Kind { get; set; }
    [Key(2)]
    public string Title { get; set; } = string.Empty;
    [Key(3)]
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// A question as the client may see it: <see cref="CorrectOption"/>, <see cref="ExpectedAnswer"/> and
/// <see cref="Feedback"/> stay empty until the question is answered.
/// </summary>
[MessagePackObject]
public class LearningQuestion
{
    [Key(0)]
    public string Concept { get; set; } = string.Empty;
    [Key(1)]
    public string Prompt { get; set; } = string.Empty;
    [Key(2)]
    public string[] Options { get; set; } = [];
    [Key(3)]
    public LessonVerdict Verdict { get; set; }
    [Key(4)]
    public int? ChosenOption { get; set; }
    [Key(5)]
    public string AnswerText { get; set; } = string.Empty;
    [Key(6)]
    public int? CorrectOption { get; set; }
    [Key(7)]
    public string ExpectedAnswer { get; set; } = string.Empty;
    [Key(8)]
    public string Feedback { get; set; } = string.Empty;

    [IgnoreMember]
    public bool IsAnswered => Verdict != LessonVerdict.Unanswered;
}

[MessagePackObject]
public class LearningLesson
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public LessonMode Mode { get; set; }
    [Key(2)]
    public LessonStatus Status { get; set; }
    /// <summary>
    /// The custom topic, or a description of the weak spots the lesson works on.
    /// </summary>
    [Key(3)]
    public string Topic { get; set; } = string.Empty;
    [Key(4)]
    public string Intro { get; set; } = string.Empty;
    [Key(5)]
    public string Closing { get; set; } = string.Empty;
    [Key(6)]
    public LearningQuestion[] Questions { get; set; } = [];
    [Key(7)]
    public DateTime CreatedAt { get; set; }
    /// <summary>
    /// Points so far: one for a correct answer, a half for a partly correct one.
    /// </summary>
    [Key(8)]
    public double Score { get; set; }

    [IgnoreMember]
    public int AnsweredCount => Questions.Count(q => q.IsAnswered);

    /// <summary>
    /// The first question still waiting for an answer; the question count when all are answered.
    /// </summary>
    [IgnoreMember]
    public int NextIndex
    {
        get
        {
            int index = Array.FindIndex(Questions, q => !q.IsAnswered);
            return index < 0 ? Questions.Length : index;
        }
    }
}

[MessagePackObject]
public class LearningOverview
{
    [Key(0)]
    public Character Tutor { get; set; } = new();
    [Key(1)]
    public LearningStruggle[] Struggles { get; set; } = [];
    /// <summary>
    /// The lesson to resume: one still being prepared or still running.
    /// </summary>
    [Key(2)]
    public LearningLesson? Current { get; set; }
    [Key(3)]
    public LearningLesson[] Recent { get; set; } = [];
}
