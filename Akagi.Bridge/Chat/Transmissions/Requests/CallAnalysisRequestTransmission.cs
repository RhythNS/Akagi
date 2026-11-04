using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum CallAnalysisAction
{
    List,
    Detail
}

[MessagePackObject]
public class CallAnalysisRequestTransmission : Transmission
{
    public override string MessageType => nameof(CallAnalysisRequestTransmission);

    /// <summary>
    /// Echoed in the response, so a list and a detail request can be under way at once.
    /// </summary>
    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public CallAnalysisAction Action { get; set; }
    [Key(2)]
    public string CallId { get; set; } = string.Empty;
}
