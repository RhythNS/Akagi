using Akagi.Characters.Cards;
using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters;

internal class Character : Savable
{
    public List<Conversation> Conversations { get; set; } = [];

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public required string CardId { get; set; }

    public Card Card
    {
        get
        {
            return _card ?? throw new InvalidOperationException("Card is not set. Ensure to set the Card property after retrieving the Character from the database.");
        }
    }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public required string PuppeteerId { get; set; }

    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public required string UserId { get; set; }

    private Card? _card;

    public void Init(Card card)
    {
        _card = card ?? throw new ArgumentNullException(nameof(card), "Card cannot be null.");
    }

    public Conversation GetCurrentConversation()
    {
        if (Conversations.Count != 0)
        {
            Conversation? conv = Conversations.MaxBy(c => c.Time);
            if (conv != null && conv.IsCompleted == false)
            {
                return conv;
            }
        }

        Conversations.Add(new Conversation
        {
            Time = DateTime.UtcNow,
            Messages = []
        });
        return Conversations[0];
    }
}
