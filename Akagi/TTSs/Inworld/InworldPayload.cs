using System.Text.Json.Serialization;

namespace Akagi.TTSs.Inworld;

internal class InworldPayload
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("voiceId")]
    public required string VoiceId { get; set; }

    [JsonPropertyName("modelId")]
    public required string ModelId { get; set; }

    [JsonPropertyName("audioConfig")]
    public AudioConfiguration? AudioConfig { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("timestampType")]
    public string? TimestampType { get; set; }

    [JsonPropertyName("applyTextNormalization")]
    public string? ApplyTextNormalization { get; set; }

    [JsonPropertyName("timestampTransportStrategy")]
    public string? TimestampTransportStrategy { get; set; }

    internal class AudioConfiguration
    {
        [JsonPropertyName("audioEncoding")]
        public string? AudioEncoding { get; set; }

        [JsonPropertyName("bitRate")]
        public int? BitRate { get; set; }

        [JsonPropertyName("sampleRateHertz")]
        public int? SampleRateHertz { get; set; }

        [JsonPropertyName("speakingRate")]
        public double? SpeakingRate { get; set; }
    }
}
