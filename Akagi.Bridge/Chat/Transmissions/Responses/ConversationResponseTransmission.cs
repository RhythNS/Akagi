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
}
