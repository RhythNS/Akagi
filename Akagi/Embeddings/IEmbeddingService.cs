namespace Akagi.Embeddings;

internal interface IEmbeddingService
{
    public int Dimension { get; }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);

    public Task<float[][]> GenerateEmbeddingsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
