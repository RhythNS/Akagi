using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class MiningResponseTransmission : Transmission
{
    public override string MessageType => nameof(MiningResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    /// <summary>
    /// All mined sentences after the action, newest first.
    /// </summary>
    [Key(1)]
    public MinedSentenceView[] Sentences { get; set; } = [];
    /// <summary>
    /// The sentence an add created or found.
    /// </summary>
    [Key(2)]
    public MinedSentenceView? Added { get; set; }
    /// <summary>
    /// The export to save, with its name.
    /// </summary>
    [Key(3)]
    public string FileName { get; set; } = string.Empty;
    [Key(4)]
    public byte[] Data { get; set; } = [];
    [Key(5)]
    public string? Error { get; set; }
}
