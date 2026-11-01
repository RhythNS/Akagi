using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class LearningResponseTransmission : Transmission
{
    public override string MessageType => nameof(LearningResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public LearningOverview? Overview { get; set; }
    [Key(2)]
    public LearningLesson? Lesson { get; set; }
    /// <summary>
    /// What went wrong, in words the user can read.
    /// </summary>
    [Key(3)]
    public string? Error { get; set; }
}
