using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.Memories;

[BsonIgnoreExtraElements]
internal class MemoryRecord : Savable
{
    private string _characterId = string.Empty;
    private string? _userId;
    private string _fact = string.Empty;
    private float[] _embedding = [];
    private float _importance = 1.0f;
    private DateTime _timestamp = DateTime.UtcNow;
    private DateTime _lastAccessed = DateTime.UtcNow;
    private bool _isArchived;
    private string? _category;

    [BsonRepresentation(BsonType.ObjectId)]
    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }

    public string Fact
    {
        get => _fact;
        set => SetProperty(ref _fact, value);
    }

    public float[] Embedding
    {
        get => _embedding;
        set => SetProperty(ref _embedding, value);
    }

    public float Importance
    {
        get => _importance;
        set => SetProperty(ref _importance, value);
    }

    public DateTime Timestamp
    {
        get => _timestamp;
        set => SetProperty(ref _timestamp, value);
    }

    public DateTime LastAccessed
    {
        get => _lastAccessed;
        set => SetProperty(ref _lastAccessed, value);
    }

    public bool IsArchived
    {
        get => _isArchived;
        set => SetProperty(ref _isArchived, value);
    }

    public string? Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public MemoryRecord Copy()
    {
        return new MemoryRecord
        {
            Id = Id,
            CharacterId = CharacterId,
            UserId = UserId,
            Fact = Fact,
            Embedding = (float[])Embedding.Clone(),
            Importance = Importance,
            Timestamp = Timestamp,
            LastAccessed = LastAccessed,
            IsArchived = IsArchived,
            Category = Category
        };
    }

    public override string ToString()
    {
        string userPart = UserId != null ? $"User: {UserId}" : "Global Lore";
        string datePart = Timestamp.ToString("yyyy-MM-dd");
        return $"[{datePart}] ({userPart}, Imp: {Importance:F1}) {Fact}";
    }
}
