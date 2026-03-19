using Akagi.Characters.VoiceClips;

namespace Akagi.TTSs;

internal interface ITTS
{
    public Task<TTSResult> SynthesizeSpeechAsync(string text, string voiceId, string modelId);

    public enum TTSType
    {
        Inworld
    }
}


internal abstract class TTS : ITTS
{
    public abstract Task<TTSResult> SynthesizeSpeechAsync(string text, string voiceId, string modelId);
}

internal class TTSResult
{
    public required byte[] AudioContent { get; init; }
    public string? UsedModelId { get; init; }
    public int ProcessedCharactersCount { get; init; }
    public required AudioEncoding AudioEncoding { get; init; }
}
