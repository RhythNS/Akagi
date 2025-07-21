using System.Text.Json.Serialization;

namespace Akagi.Connectors.Tatoeba;

internal class TatoebaResponse
{
    [JsonPropertyName("data")]
    public List<Example>? Data { get; set; }

    [JsonPropertyName("paging")]
    public Paging? Paging { get; set; }
}

internal class Example
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("lang")]
    public string? Language { get; set; }

    [JsonPropertyName("script")]
    public string? Script { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("transcriptions")]
    public List<Transcription>? Transcriptions { get; set; }

    [JsonPropertyName("audios")]
    public List<Audio>? Audios { get; set; }

    [JsonPropertyName("translations")]
    public List<List<Translation>>? Translations { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

internal class Transcription
{
    [JsonPropertyName("script")]
    public string? Script { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("needsReview")]
    public bool? NeedsReview { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("html")]
    public string? Html { get; set; }
}

internal class Translation
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("lang")]
    public string? Language { get; set; }

    [JsonPropertyName("script")]
    public string? Script { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("transcriptions")]
    public List<Transcription>? Transcriptions { get; set; }

    [JsonPropertyName("audios")]
    public List<Audio>? Audios { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

internal class Audio
{
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("attribution_url")]
    public string? AttributionUrl { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }
}

internal class Paging
{
    [JsonPropertyName("total")]
    public long? Total { get; set; }

    [JsonPropertyName("has_next")]
    public bool? HasNext { get; set; }

    [JsonPropertyName("cursor_end")]
    public string? CursorEnd { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }
}
