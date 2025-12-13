using MongoDB.Bson.Serialization;

namespace Akagi.Utils.Extensions;

internal static class BsonExtensions
{
    public static void Register(Type type)
    {
        if (BsonClassMap.IsClassMapRegistered(type))
        {
            return;
        }

        BsonClassMap classMap = new(type);
        classMap.AutoMap();
        BsonClassMap.RegisterClassMap(classMap);
    }
}
