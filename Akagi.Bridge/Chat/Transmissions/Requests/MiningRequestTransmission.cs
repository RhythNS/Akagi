using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum MiningAction
{
    /// <summary>The user's mined sentences, so lines already mined link to them.</summary>
    List,
    /// <summary>
    /// Mines the line of <see cref="MiningRequestTransmission.CallId"/> at <see cref="MiningRequestTransmission.Time"/>
    /// and starts cutting its clip.
    /// </summary>
    Add,
    /// <summary>One sentence, to follow its clip being cut.</summary>
    Get,
    /// <summary>One of <see cref="MiningFileKind"/> for the sentence <see cref="MiningRequestTransmission.Id"/>.</summary>
    Download,
    /// <summary>Cuts the clip again after it failed.</summary>
    Retry,
    Remove,
    /// <summary>A zip of one call's SRT subtitles and audio.</summary>
    ExportCall
}

/// <summary>
/// The downloads of one mined sentence.
/// </summary>
public enum MiningFileKind
{
    /// <summary>The clip on its own.</summary>
    Audio,
    /// <summary>An SRT with the sentence, timed to the clip.</summary>
    Subtitles,
    /// <summary>A zip with the clip and a one-card Anki import file.</summary>
    Anki
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
    public string Id { get; set; } = string.Empty;
    [Key(7)]
    public MiningFileKind File { get; set; }
    [Key(8)]
    public MiningVoiceChoice Voices { get; set; }
}
