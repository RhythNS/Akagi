using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Web.Data;

public abstract class Savable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
}
