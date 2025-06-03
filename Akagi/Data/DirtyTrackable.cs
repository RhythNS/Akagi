using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Data;

internal class DirtyTrackable
{
    [BsonIgnore]
    public virtual bool Dirty { get; set; } = false;

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Dirty = true;
            return true;
        }
        return false;
    }
}
