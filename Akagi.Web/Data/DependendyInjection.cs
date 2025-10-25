using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace Akagi.Web.Data;

static class DependendyInjection
{
    public static void AddData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection("MongoDB"));
        services.AddSingleton<IUserDatabase, UserDatabase>();
        services.AddSingleton<IEntryDatabase, EntryDatabase>();
        services.AddSingleton<IDefinitionDatabase, DefinitionDatabase>();
        BsonSerializer.RegisterSerializer(typeof(Dictionary<string, object>), new DictionaryStringObjectBsonSerializer());
    }
}

public class DictionaryStringObjectBsonSerializer : IBsonSerializer<Dictionary<string, object>>
{
    public Type ValueType => typeof(Dictionary<string, object>);

    public Dictionary<string, object> Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        IBsonReader bsonReader = context.Reader;
        Dictionary<string, object> dict = [];

        bsonReader.ReadStartDocument();
        while (bsonReader.ReadBsonType() != BsonType.EndOfDocument)
        {
            string name = bsonReader.ReadName();
            BsonType bsonType = bsonReader.GetCurrentBsonType();

            object value = bsonType switch
            {
                BsonType.Boolean => bsonReader.ReadBoolean(),
                BsonType.Int32 => bsonReader.ReadInt32(),
                BsonType.Int64 => bsonReader.ReadInt64(),
                BsonType.Double => bsonReader.ReadDouble(),
                BsonType.String => bsonReader.ReadString(),
                BsonType.DateTime => DateTime.SpecifyKind(BsonUtils.ToDateTimeFromMillisecondsSinceEpoch(bsonReader.ReadDateTime()), DateTimeKind.Utc),
                BsonType.Document => BsonSerializer.Deserialize<Dictionary<string, object>>(bsonReader),
                _ => BsonSerializer.Deserialize<object>(bsonReader)
            };

            // Handle TimeOnly and DateOnly stored as string
            if (value is string str)
            {
                if (TimeOnly.TryParse(str, out TimeOnly t))
                    value = t;
                else if (DateOnly.TryParse(str, out DateOnly d))
                    value = d;
            }

            dict[name] = value;
        }
        bsonReader.ReadEndDocument();
        return dict;
    }

    public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Dictionary<string, object> value)
    {
        IBsonWriter writer = context.Writer;
        writer.WriteStartDocument();
        foreach (KeyValuePair<string, object> kvp in value)
        {
            writer.WriteName(kvp.Key);
            switch (kvp.Value)
            {
                case null:
                    writer.WriteNull();
                    break;
                case TimeOnly t:
                    writer.WriteString(t.ToString("HH:mm:ss.fffffff"));
                    break;
                case DateOnly d:
                    writer.WriteString(d.ToString("yyyy-MM-dd"));
                    break;
                default:
                    BsonSerializer.Serialize(writer, kvp.Value?.GetType() ?? typeof(object), kvp.Value);
                    break;
            }
        }
        writer.WriteEndDocument();
    }

    object IBsonSerializer.Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        => Deserialize(context, args);

    void IBsonSerializer.Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
        => Serialize(context, args, (Dictionary<string, object>)value);
}
