using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum GoogleHealthAction
{
    /// <summary>How the link stands.</summary>
    Status,
    /// <summary>Starts a new link and returns Google's consent page.</summary>
    Start,
    /// <summary>Finishes the link with <see cref="GoogleHealthRequestTransmission.Code"/> and syncs once.</summary>
    Complete,
    /// <summary>Syncs now.</summary>
    Sync
}

[MessagePackObject]
public class GoogleHealthRequestTransmission : Transmission
{
    public override string MessageType => nameof(GoogleHealthRequestTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public GoogleHealthAction Action { get; set; }
    /// <summary>
    /// The code, or the whole address the browser landed on after consent.
    /// </summary>
    [Key(2)]
    public string Code { get; set; } = string.Empty;
}
