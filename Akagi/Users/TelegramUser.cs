using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Users;

internal class TelegramUser
{
    public long Id { get; set; } = -1;
    public string UserName { get; set; } = string.Empty;
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string? CurrentCharacterId { get; set; } = null;
}
