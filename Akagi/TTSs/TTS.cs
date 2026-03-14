namespace Akagi.TTSs;

internal interface ITTS
{
    public Task<TTSResult> SynthesizeSpeechAsync(string text, string voiceId, string modelId);

    public enum AudioEncoding
    {
        LINEAR16,
        MP3,
        OGG_OPUS,
        ALAW,
        MULAW,
        FLAC,
        PCM,
        WAV
    }

    public enum TTSType
    {
        Inworld
    }
}

internal static class AudioEncodingExtensions
{
    public static string ToFile(this ITTS.AudioEncoding encoding)
    {
        return encoding switch
        {
            ITTS.AudioEncoding.LINEAR16 => ".wav",
            ITTS.AudioEncoding.MP3 => ".mp3",
            ITTS.AudioEncoding.OGG_OPUS => ".ogg",
            ITTS.AudioEncoding.ALAW => ".alaw",
            ITTS.AudioEncoding.MULAW => ".mulaw",
            ITTS.AudioEncoding.FLAC => ".flac",
            ITTS.AudioEncoding.PCM => ".pcm",
            ITTS.AudioEncoding.WAV => ".wav",
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null)
        };
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
    public required ITTS.AudioEncoding AudioEncoding { get; init; }
}
