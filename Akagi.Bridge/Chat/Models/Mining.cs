using MessagePack;

namespace Akagi.Bridge.Chat.Models;

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
    public string Note { get; set; } = string.Empty;
    [Key(11)]
    public DateTime AddedAt { get; set; }
}
