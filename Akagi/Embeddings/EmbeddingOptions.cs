namespace Akagi.Embeddings;

internal class EmbeddingOptions
{
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1/";

    public string Model { get; set; } = "qwen/qwen3-embedding-8b";

    public int Dimension { get; set; } = 1536;
}
    