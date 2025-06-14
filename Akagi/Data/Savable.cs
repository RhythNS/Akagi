using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Data;

internal abstract class Savable : DirtyTrackable
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public bool New => string.IsNullOrEmpty(Id) || Id == ObjectId.Empty.ToString();

    public Task AfterLoad()
    {
        Dirty = false;
        return InnerAfterLoad();
    }

    protected virtual Task InnerAfterLoad() => Task.CompletedTask;
}
