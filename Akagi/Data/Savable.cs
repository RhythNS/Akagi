using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Akagi.Data;

internal abstract class Savable : DirtyTrackable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public bool New => string.IsNullOrEmpty(Id) || Id == ObjectId.Empty.ToString();
}
