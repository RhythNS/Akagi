using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum CallAnalysisAction
{
    List,
    Detail,
    /// <summary>The user's say on one finding: useful, not a mistake, or neither.</summary>
    Feedback,
    /// <summary>Trends, vocabulary and recurring struggles across all calls.</summary>
    Insights,
    /// <summary>The people the user talked to in calls.</summary>
    People,
    /// <summary>Everything known about one person, by <see cref="CallAnalysisRequestTransmission.Name"/>.</summary>
    Person,
    /// <summary>Corrects the day a call took place to <see cref="CallAnalysisRequestTransmission.Date"/>.</summary>
    SetDate,
    /// <summary>Deletes a finished call and takes back everything it added to the diagnosis, people and memories.</summary>
    Delete,
    /// <summary>Marks the struggle <see cref="CallAnalysisRequestTransmission.IssueId"/> as a focus struggle, or unmarks it.</summary>
    SetFocus,
    /// <summary>Asks the Learning model for a written study plan; answers with the insights.</summary>
    Advice,
    /// <summary>
    /// Hides the suggestion <see cref="CallAnalysisRequestTransmission.AdviceKey"/> from "What to work on", or shows it
    /// again when <see cref="CallAnalysisRequestTransmission.Ignore"/> is false; answers with the insights.
    /// </summary>
    IgnoreAdvice
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
    [Key(3)]
    public string FindingId { get; set; } = string.Empty;
    [Key(4)]
    public CallFindingFeedback Feedback { get; set; }
    [Key(5)]
    public string Name { get; set; } = string.Empty;
    [Key(6)]
    public DateTime? Date { get; set; }
    /// <summary>
    /// Insights only over calls from this moment on (UTC, inclusive), e.g. the start of a local day.
    /// </summary>
    [Key(7)]
    public DateTime? From { get; set; }
    /// <summary>
    /// Insights only over calls before this moment (UTC, exclusive), e.g. the start of the day after the last one.
    /// </summary>
    [Key(8)]
    public DateTime? To { get; set; }
    [Key(9)]
    public string IssueId { get; set; } = string.Empty;
    [Key(10)]
    public bool Focus { get; set; }
    /// <summary>
    /// <see cref="InsightAdvice.Key"/> of the suggestion to ignore.
    /// </summary>
    [Key(11)]
    public string AdviceKey { get; set; } = string.Empty;
    [Key(12)]
    public bool Ignore { get; set; }
}
