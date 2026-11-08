using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class ConjugationResponseTransmission : Transmission
{
    public override string MessageType => nameof(ConjugationResponseTransmission);

    [Key(0)]
    public string RequestId { get; set; } = string.Empty;
    [Key(1)]
    public ConjugationSettingsView Settings { get; set; } = new();
    [Key(2)]
    public ConjugationVerbView[] Verbs { get; set; } = [];
    [Key(3)]
    public ConjugationItemView[] Items { get; set; } = [];
    /// <summary>
    /// Answers per day, the last 30 days with answers, oldest first.
    /// </summary>
    [Key(4)]
    public ConjugationDayView[] Days { get; set; } = [];
    /// <summary>
    /// The latest wrong answers, newest first.
    /// </summary>
    [Key(5)]
    public ConjugationAttemptView[] Mistakes { get; set; } = [];
    /// <summary>
    /// The item a <c>Record</c> changed.
    /// </summary>
    [Key(6)]
    public ConjugationItemView? Item { get; set; }
    /// <summary>
    /// The dictionary is still being read after the bot started; only verbs from calls and progress are there yet.
    /// </summary>
    [Key(7)]
    public bool DictionaryLoading { get; set; }
    [Key(8)]
    public string? Error { get; set; }
}
