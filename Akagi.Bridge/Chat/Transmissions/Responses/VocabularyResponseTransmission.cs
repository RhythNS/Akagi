using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class VocabularyResponseTransmission : Transmission
{
    public override string MessageType => nameof(VocabularyResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public VocabCatalog? Catalog { get; set; }
    [Key(2)]
    public VocabReportResult? Report { get; set; }
    [Key(3)]
    public VocabWordDetail? Word { get; set; }
    /// <summary>
    /// What went wrong, in words the user can read.
    /// </summary>
    [Key(4)]
    public string? Error { get; set; }
    [Key(5)]
    public VocabExpressionResult? Expression { get; set; }
}
