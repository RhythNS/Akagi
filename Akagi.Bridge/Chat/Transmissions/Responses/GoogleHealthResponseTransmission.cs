using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class GoogleHealthResponseTransmission : Transmission
{
    public override string MessageType => nameof(GoogleHealthResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public GoogleHealthStatus Status { get; set; } = new();
    /// <summary>
    /// Google's consent page, only for <see cref="Requests.GoogleHealthAction.Start"/>.
    /// </summary>
    [Key(2)]
    public string? AuthorizationUrl { get; set; }
    /// <summary>
    /// What a link or sync did.
    /// </summary>
    [Key(3)]
    public string? Message { get; set; }
    /// <summary>
    /// What went wrong, in words the user can read.
    /// </summary>
    [Key(4)]
    public string? Error { get; set; }
}
