using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum LearningAction
{
    Overview,
    Start,
    Answer,
    Finish
}

[MessagePackObject]
public class LearningRequestTransmission : Transmission
{
    public override string MessageType => nameof(LearningRequestTransmission);

    /// <summary>
    /// Echoed in the response; several requests can be under way, and starting a lesson takes a while.
    /// </summary>
    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public LearningAction Action { get; set; }
    [Key(2)]
    public LessonMode Mode { get; set; }
    /// <summary>
    /// A custom topic; when empty the lesson works on the user's weak spots.
    /// </summary>
    [Key(3)]
    public string Topic { get; set; } = string.Empty;
    /// <summary>
    /// The weak spots to work on; when empty the most pressing ones are chosen.
    /// </summary>
    [Key(4)]
    public string[] FocusIds { get; set; } = [];
    [Key(5)]
    public int QuestionCount { get; set; }
    [Key(6)]
    public string LessonId { get; set; } = string.Empty;
    [Key(7)]
    public int QuestionIndex { get; set; }
    [Key(8)]
    public int? ChosenOption { get; set; }
    [Key(9)]
    public string AnswerText { get; set; } = string.Empty;
}
