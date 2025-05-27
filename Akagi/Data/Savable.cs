using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace Akagi.Data;

internal abstract class Savable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
}
