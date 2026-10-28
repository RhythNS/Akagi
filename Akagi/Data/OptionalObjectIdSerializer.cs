using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Akagi.Data;

/// <summary>
/// Stores a string id as an ObjectId like [BsonRepresentation(BsonType.ObjectId)], but an empty string is stored as
/// null instead of failing. Used for ids that may be unset, such as the image of a card made in the editor.
/// </summary>
internal class OptionalObjectIdSerializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        IBsonReader reader = context.Reader;
        switch (reader.GetCurrentBsonType())
        {
            case BsonType.Null:
                reader.ReadNull();
                return string.Empty;
            case BsonType.ObjectId:
                return reader.ReadObjectId().ToString();
            case BsonType.String:
                return reader.ReadString();
            default:
                throw new FormatException($"Cannot read an id from BSON type {reader.GetCurrentBsonType()}.");
        }
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            context.Writer.WriteNull();
            return;
        }
        if (!ObjectId.TryParse(value, out ObjectId id))
        {
            throw new FormatException($"'{value}' is not a valid 24 digit hex string.");
        }
        context.Writer.WriteObjectId(id);
    }
}
