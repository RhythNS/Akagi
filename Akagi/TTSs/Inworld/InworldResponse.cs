using System.Text.Json.Serialization;

namespace Akagi.TTSs.Inworld;

internal class InworldStreamChunk
{
    [JsonPropertyName("result")]
    public InworldResult? Result { get; set; }

    [JsonPropertyName("error")]
    public InworldError? Error { get; set; }
}

internal class InworldResult
{
    [JsonPropertyName("audioContent")]
    public string? AudioContent { get; set; }

    [JsonPropertyName("usage")]
    public InworldUsage? Usage { get; set; }
}

internal class InworldUsage
{
    [JsonPropertyName("processedCharactersCount")]
    public int ProcessedCharactersCount { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }
}

internal class InworldError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    public List<object>? Details { get; set; }
}
