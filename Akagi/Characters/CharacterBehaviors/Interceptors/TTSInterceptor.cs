using Akagi.Characters.Conversations;
using Akagi.Characters.VoiceClips;
using Akagi.Flow;
using Akagi.TTSs;
using Akagi.TTSs.Inworld;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization.Attributes;

namespace Akagi.Characters.CharacterBehaviors.Interceptors;

internal class TTSInterceptor : Interceptor
{
    private string _voiceId = string.Empty;
    private string _modelId = string.Empty;
    private AudioEncoding _audioEncoding = AudioEncoding.MP3;

    public string VoiceId
    {
        get => _voiceId;
        set => SetProperty(ref _voiceId, value);
    }

    public string ModelId
    {
        get => _modelId;
        set => SetProperty(ref _modelId, value);
    }

    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public AudioEncoding AudioEncoding
    {
        get => _audioEncoding;
        set => SetProperty(ref _audioEncoding, value);
    }

    public override async Task SendMessageAsync(User user, Character character, Message message,
        Func<User, Character, Message, Task> next)
    {
        await next(user, character, message);

        if (character.AllowVoice == false)
        {
            return;
        }
        if (message is not TextMessage textMessage || string.IsNullOrWhiteSpace(textMessage.Text))
        {
            return;
        }

        IInworldTTSClient tts = Globals.Instance.ServiceProvider.GetRequiredService<IInworldTTSClient>();
        TTSResult result = await tts.SynthesizeSpeechAsync(textMessage.Text, _voiceId, _modelId);

        using MemoryStream stream = new(result.AudioContent);
        await Communicator.SendAudio(user, character, stream, $"audio{result.AudioEncoding.ToFile()}");
    }
}
