using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

/// <summary>
/// Live progress of a chat (a character or a group): a turn started, a step was added, or the turn ended. Lets the
/// website show what is happening while the characters are still working.
/// </summary>
[MessagePackObject]
public class ChatActivityResponseTransmission : Transmission
{
    public enum ActivityKind
    {
        TurnStarted = 0,
        Step = 1,
        TurnEnded = 2
    }

    public override string MessageType => nameof(ChatActivityResponseTransmission);

    /// <summary>The character or group whose chat this is.</summary>
    [Key(0)]
    public string ChatId { get; set; } = string.Empty;

    [Key(1)]
    public ActivityKind Kind { get; set; }

    /// <summary>The message that was added, for <see cref="ActivityKind.Step"/>.</summary>
    [Key(2)]
    public TextMessage? Message { get; set; }
}
