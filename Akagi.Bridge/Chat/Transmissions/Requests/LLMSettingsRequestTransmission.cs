using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum LLMSettingsAction
{
    /// <summary>The user's settings and every model that can be picked.</summary>
    Overview,
    /// <summary>Lets <see cref="LLMSettingsRequestTransmission.Usage"/> run on the given model.</summary>
    Set,
    /// <summary>Drops the preference for <see cref="LLMSettingsRequestTransmission.Usage"/>, so the default applies.</summary>
    Reset
}

[MessagePackObject]
public class LLMSettingsRequestTransmission : Transmission
{
    public override string MessageType => nameof(LLMSettingsRequestTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public LLMSettingsAction Action { get; set; }
    [Key(2)]
    public string Usage { get; set; } = string.Empty;
    [Key(3)]
    public string Provider { get; set; } = string.Empty;
    [Key(4)]
    public string Model { get; set; } = string.Empty;
}
