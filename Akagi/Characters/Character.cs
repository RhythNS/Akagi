using Akagi.Characters.Cards;
using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters;

internal class Character : Savable
{
    public List<Conversation> Conversations { get; set; } = [];

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string CardId { get; set; }

    [BsonIgnore]
    public Card Card { get; set; }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string SystemProcessorId { get; set; } = string.Empty;

    public Conversation? GetLastConversation() => Conversations.MaxBy(c => c.Time);
}
