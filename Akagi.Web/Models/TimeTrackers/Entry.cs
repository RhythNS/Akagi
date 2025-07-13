using Akagi.Web.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Web.Models.TimeTrackers;

public class Entry : Savable
{
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public required string DefinitionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object> Values { get; set; } = [];
}
