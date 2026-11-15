using Akagi.Bridge.Chat.Models;
using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Responses;

[MessagePackObject]
public class ConversationResponseTransmission : Transmission
{
    public override string MessageType => nameof(ConversationResponseTransmission);

    [Key(0)]
    public required string CharacterId { get; set; } = string.Empty;

    [Key(1)]
    public required int? ConversationId { get; set; } = null;

    [Key(2)]
    public required Conversation[] Conversations { get; set; } = [];

    /// <summary>A turn is running in this chat right now; its steps arrive as chat activity.</summary>
    [Key(3)]
    public bool IsBusy { get; set; }
}
