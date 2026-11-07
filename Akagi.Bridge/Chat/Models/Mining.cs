using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// How far the clip of a mined sentence is.
/// </summary>
public enum MinedClipState
{
    /// <summary>Waiting for its turn.</summary>
    Queued,
    Cutting,
    Ready,
    /// <summary>The call has no recording (a transcript), so only the text downloads exist.</summary>
    NoAudio,
    Failed
}

/// <summary>
/// A sentence from a call the user picked to learn from.
/// </summary>
[MessagePackObject]
public class MinedSentenceView
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
    [Key(1)]
    public string CallId { get; set; } = string.Empty;
    [Key(2)]
    public string CallName { get; set; } = string.Empty;
    [Key(3)]
    public DateTime Date { get; set; }
    [Key(4)]
    public double Start { get; set; }
    [Key(5)]
    public double End { get; set; }
    [Key(6)]
    public string Speaker { get; set; } = string.Empty;
    [Key(7)]
    public bool IsLearner { get; set; }
    [Key(8)]
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// The word or expression it was mined for.
    /// </summary>
    [Key(9)]
    public string Target { get; set; } = string.Empty;
    [Key(10)]
    public DateTime AddedAt { get; set; }
    [Key(11)]
    public MinedClipState State { get; set; }
    /// <summary>
    /// Why the clip could not be cut.
    /// </summary>
    [Key(12)]
    public string Error { get; set; } = string.Empty;
    /// <summary>
    /// Seconds of the clip, once cut.
    /// </summary>
    [Key(13)]
    public double ClipSeconds { get; set; }
}
