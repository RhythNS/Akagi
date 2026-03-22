using Akagi.Bridge.Attributes;
using Akagi.Characters;
using Akagi.Characters.Groups;
using Akagi.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Users;

internal class TelegramUser : DirtyTrackable
{
    private long _id = -1;
    private string _userName = string.Empty;
    private string? _currentCharacterId = null;
    private string? _currentGroupId = null;

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
    [NodeReference(typeof(Character))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string? CurrentCharacterId
    {
        get => _currentCharacterId;
        set
        {
            if (string.IsNullOrEmpty(value) == false)
            {
                CurrentGroupId = null;
            }
            SetProperty(ref _currentCharacterId, value);
        }
    }
    [NodeReference(typeof(CharacterGroup))]
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string? CurrentGroupId
    {
        get => _currentGroupId;
        set
        {
            if (string.IsNullOrEmpty(value) == false)
            {
                CurrentCharacterId = null;
            }
            SetProperty(ref _currentGroupId, value);
        }
    }
}
