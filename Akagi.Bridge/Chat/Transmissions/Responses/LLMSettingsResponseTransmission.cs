using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class LLMSettingsResponseTransmission : Transmission
{
    public override string MessageType => nameof(LLMSettingsResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public LLMUsageSetting[] Usages { get; set; } = [];
    /// <summary>
    /// Only sent for <see cref="Requests.LLMSettingsAction.Overview"/>; the list is large.
    /// </summary>
    [Key(2)]
    public LLMModel[] Models { get; set; } = [];
    /// <summary>
    /// Providers whose models could not be listed.
    /// </summary>
    [Key(3)]
    public string[] ModelErrors { get; set; } = [];
    /// <summary>
    /// What went wrong, in words the user can read.
    /// </summary>
    [Key(4)]
    public string? Error { get; set; }
}
