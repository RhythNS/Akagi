using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// Where an expression was said across the filtered calls: how often by the user and by partners, per call and line.
/// </summary>
[MessagePackObject]
public class VocabExpressionResult
{
    [Key(0)]
    public string Query { get; set; } = string.Empty;
    [Key(1)]
    public int Learner { get; set; }
    [Key(2)]
    public int Partner { get; set; }
    /// <summary>
    /// Times per hour of the user's own speaking, and of the partners' speaking; null without speaking time.
    /// </summary>
    [Key(3)]
    public double? LearnerPerHour { get; set; }
    [Key(4)]
    public double? PartnerPerHour { get; set; }
    /// <summary>
    /// Every call in the filter, in date order, also those without a match.
    /// </summary>
    [Key(5)]
    public VocabExpressionCall[] Calls { get; set; } = [];
    /// <summary>
    /// The lines it was said in, newest call first and in call order within a call. <see cref="VocabLine.Marks"/> holds
    /// the text as it was matched.
    /// </summary>
    [Key(6)]
    public VocabLine[] Lines { get; set; } = [];
    /// <summary>
    /// Lines were capped; this many exist.
    /// </summary>
    [Key(7)]
    public int TotalLines { get; set; }
}

[MessagePackObject]
public class VocabExpressionCall
{
    [Key(0)]
    public string CallId { get; set; } = string.Empty;
    [Key(1)]
    public string Name { get; set; } = string.Empty;
    [Key(2)]
    public DateTime Date { get; set; }
    [Key(3)]
    public int Learner { get; set; }
    [Key(4)]
    public int Partner { get; set; }
    [Key(5)]
    public double? LearnerPerHour { get; set; }
    [Key(6)]
    public double? PartnerPerHour { get; set; }
    /// <summary>
    /// The partner's voice was transcribed too, so their count means something.
    /// </summary>
    [Key(7)]
    public bool HasPartner { get; set; }
}
