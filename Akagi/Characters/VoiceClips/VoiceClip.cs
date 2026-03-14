using Akagi.Data;
using Akagi.TTSs;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.VoiceClips;

internal class VoiceClip : Savable
{
    private string _characterId = string.Empty;
    private string _characterName = string.Empty;
    private string _text = string.Empty;
    private string _audioId = string.Empty;
    private ITTS.AudioEncoding _audioEncoding = ITTS.AudioEncoding.MP3;

    public string CharacterId
    {
        get => _characterId;
        set => SetProperty(ref _characterId, value);
    }
    public string CharacterName
    {
        get => _characterName;
        set => SetProperty(ref _characterName, value);
    }
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }
    [BsonRepresentation(BsonType.ObjectId)]
    public string AudioId
    {
        get => _audioId;
        set => SetProperty(ref _audioId, value);
    }
    public ITTS.AudioEncoding AudioEncoding
    {
        get => _audioEncoding;
        set => SetProperty(ref _audioEncoding, value);
    }
}
