using Akagi.Data;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.VoiceClips;

internal class VoiceClip : Savable
{
    private string? _text = null;
    private string _audioId = string.Empty;
    private AudioEncoding _audioEncoding = AudioEncoding.MP3;

    public string? Text
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
    public AudioEncoding AudioEncoding
    {
        get => _audioEncoding;
        set => SetProperty(ref _audioEncoding, value);
    }
}
