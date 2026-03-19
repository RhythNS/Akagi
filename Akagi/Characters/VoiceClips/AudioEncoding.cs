namespace Akagi.Characters.VoiceClips;

internal enum AudioEncoding
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

internal static class AudioEncodingExtensions
{
    public static string ToFile(this AudioEncoding encoding)
    {
        return $".{encoding.ToExtension()}";
    }

    public static string ToExtension(this AudioEncoding encoding)
    {
        return encoding switch
        {
            AudioEncoding.LINEAR16 => "wav",
            AudioEncoding.MP3 => "mp3",
            AudioEncoding.OGG_OPUS => "ogg",
            AudioEncoding.ALAW => "alaw",
            AudioEncoding.MULAW => "mulaw",
            AudioEncoding.FLAC => "flac",
            AudioEncoding.PCM => "pcm",
            AudioEncoding.WAV => "wav",
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null)
        };
    }
}
