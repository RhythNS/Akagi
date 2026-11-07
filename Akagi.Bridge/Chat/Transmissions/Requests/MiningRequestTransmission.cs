using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum MiningAction
{
    /// <summary>The user's mined sentences, newest first.</summary>
    List,
    /// <summary>Mines the line of <see cref="MiningRequestTransmission.CallId"/> at <see cref="MiningRequestTransmission.Time"/>.</summary>
    Add,
    Remove,
    SetNote,
    /// <summary>A zip of audio clips and an Anki import file, for <see cref="MiningRequestTransmission.Ids"/> or all.</summary>
    ExportClips,
    /// <summary>A zip of one call's SRT subtitles and audio.</summary>
    ExportCall
}

/// <summary>
/// Which voices a call export holds.
/// </summary>
public enum MiningVoiceChoice
{
    Partner,
    Both
}

[MessagePackObject]
public class MiningRequestTransmission : Transmission
{
    public override string MessageType => nameof(MiningRequestTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public MiningAction Action { get; set; }
    [Key(2)]
    public string CallId { get; set; } = string.Empty;
    /// <summary>
    /// Seconds into the call where the line starts.
    /// </summary>
    [Key(3)]
    public double Time { get; set; }
    /// <summary>
    /// The line or the sentence of it to mine.
    /// </summary>
    [Key(4)]
    public string Text { get; set; } = string.Empty;
    [Key(5)]
    public string Target { get; set; } = string.Empty;
    [Key(6)]
    public string Note { get; set; } = string.Empty;
    [Key(7)]
    public string Id { get; set; } = string.Empty;
    [Key(8)]
    public string[] Ids { get; set; } = [];
    [Key(9)]
    public MiningVoiceChoice Voices { get; set; }
}
