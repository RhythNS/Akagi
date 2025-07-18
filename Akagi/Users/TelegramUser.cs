using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Users;

internal class TelegramUser : DirtyTrackable
{
    private long _id = -1;
    private string _userName = string.Empty;
    private string? _currentCharacterId = null;

    public long Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }
    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string? CurrentCharacterId
    {
        get => _currentCharacterId;
        set => SetProperty(ref _currentCharacterId, value);
    }
}
