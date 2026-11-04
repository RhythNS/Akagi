using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class CallAnalysisResponseTransmission : Transmission
{
    public override string MessageType => nameof(CallAnalysisResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>
    /// The user's calls, newest first.
    /// </summary>
    [Key(1)]
    public CallAnalysisSummary[] Calls { get; set; } = [];
    [Key(2)]
    public CallAnalysisDetail? Detail { get; set; }
    /// <summary>
    /// What went wrong, in words the user can read.
    /// </summary>
    [Key(3)]
    public string? Error { get; set; }
}
