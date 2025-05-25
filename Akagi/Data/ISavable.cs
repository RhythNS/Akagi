using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Data;

internal interface ISavable
{
    [BsonId]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string? Id { get; set; }
}
