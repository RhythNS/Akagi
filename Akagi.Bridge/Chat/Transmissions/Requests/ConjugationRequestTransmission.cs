using Akagi.Bridge.Chat.Models;
using Akagi.Bridge.Japanese;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

public enum ConjugationAction
{
    /// <summary>Settings, the verbs to draw from, all progress, the last days and the latest mistakes.</summary>
    Load,
    /// <summary>Stores one answer and returns the item's progress.</summary>
    Record,
    SaveSettings,
    /// <summary>Forgets all progress; the settings stay.</summary>
    Reset
}

[MessagePackObject]
public class ConjugationRequestTransmission : Transmission
{
    public override string MessageType => nameof(ConjugationRequestTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public ConjugationAction Action { get; set; }
    [Key(2)]
    public ConjugationSettingsView? Settings { get; set; }
    /// <summary>
    /// The verb answered, see <see cref="ConjugationVerbView.Id"/>.
    /// </summary>
    [Key(3)]
    public string Verb { get; set; } = string.Empty;
    [Key(4)]
    public string Written { get; set; } = string.Empty;
    [Key(5)]
    public string Reading { get; set; } = string.Empty;
    [Key(6)]
    public VerbClass Class { get; set; }
    [Key(7)]
    public ConjugationForm Form { get; set; }
    [Key(8)]
    public bool Reverse { get; set; }
    [Key(9)]
    public string Answer { get; set; } = string.Empty;
    [Key(10)]
    public bool Correct { get; set; }
    [Key(11)]
    public int Millis { get; set; }
    /// <summary>
    /// Minutes the user's clock is ahead of UTC, so days start at their midnight.
    /// </summary>
    [Key(12)]
    public int UtcOffsetMinutes { get; set; }
}
