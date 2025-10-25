using MessagePack;

namespace Akagi.Bridge.Chat.Transmissions.Requests;

[MessagePackObject]
public class ConversationRequestTransmission : Transmission
{
    public override string MessageType => nameof(ConversationRequestTransmission);

    [Key(0)]
    public int? ConversationId { get; set; }

    [Key(1)]
    public string? CharacterId { get; set; } = string.Empty;
}
