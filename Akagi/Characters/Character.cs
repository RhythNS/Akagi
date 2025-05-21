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

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    public Conversation? GetLastConversation()
    {
        if (Conversations.Count != 0)
        {
            return Conversations.MaxBy(c => c.Time);
        }
        
        Conversations.Add(new Conversation
        {
            Time = DateTime.UtcNow,
            Messages = []
        });
        return Conversations[0];
    }
}
