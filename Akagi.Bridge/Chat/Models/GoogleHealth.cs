using MessagePack;

namespace Akagi.Bridge.Chat.Models;

/// <summary>
/// The user's Google Health link as the website shows it.
/// </summary>
[MessagePackObject]
public class GoogleHealthStatus
{
    /// <summary>
    /// The server has an OAuth client; without one nothing can be linked.
    /// </summary>
    [Key(0)]
    public bool Configured { get; set; }
    /// <summary>
    /// The server holds a refresh token. It can still have run out without the server noticing yet.
    /// </summary>
    [Key(1)]
    public bool Linked { get; set; }
    [Key(2)]
    public DateTime? LinkedAtUtc { get; set; }
    /// <summary>
    /// When Google is expected to drop the link; null when it does not run out.
    /// </summary>
    [Key(3)]
    public DateTime? ExpiresAtUtc { get; set; }
    [Key(4)]
    public DateTime? LastSyncUtc { get; set; }
    [Key(5)]
    public string? LastError { get; set; }
}
