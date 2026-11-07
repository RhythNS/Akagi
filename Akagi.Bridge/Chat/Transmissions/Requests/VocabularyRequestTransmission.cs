using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum VocabularyAction
{
    /// <summary>The reports, calls and partners.</summary>
    Catalog,
    /// <summary>One report under the filter.</summary>
    Report,
    /// <summary>Everything about one word under the filter.</summary>
    Word,
    /// <summary>Where an expression (any text, e.g. それも) was said, by whom and how often.</summary>
    Expression
}

[MessagePackObject]
public class VocabularyRequestTransmission : Transmission
{
    public override string MessageType => nameof(VocabularyRequestTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public VocabularyAction Action { get; set; }
    [Key(2)]
    public string ReportKey { get; set; } = string.Empty;
    [Key(3)]
    public VocabFilter Filter { get; set; } = new();
    /// <summary>
    /// The report's option values by option key.
    /// </summary>
    [Key(4)]
    public Dictionary<string, string> Options { get; set; } = [];
    [Key(5)]
    public string Lemma { get; set; } = string.Empty;
    /// <summary>
    /// The expression to look for.
    /// </summary>
    [Key(6)]
    public string Query { get; set; } = string.Empty;
}
