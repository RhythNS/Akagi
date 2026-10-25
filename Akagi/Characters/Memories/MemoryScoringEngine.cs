using Akagi.Utils;

namespace Akagi.Characters.Memories;

internal class ScoredMemory
{
    public required MemoryRecord Record { get; init; }
    public required float Similarity { get; init; }
    public required float RecencyScore { get; init; }
    public required float ImportanceScore { get; init; }
    public required float FinalScore { get; init; }
}

internal static class MemoryScoringEngine
{
    /// <summary>
    /// Calculates the multi-factor Generative Agent score for a single memory record.
    /// FinalScore = (w_rel * Similarity) + (w_rec * Recency) + (w_imp * Importance)
    /// </summary>
    public static ScoredMemory Score(
        MemoryRecord record,
        ReadOnlySpan<float> queryEmbedding,
        DateTime nowUtc,
        float relevanceWeight = 1.0f,
        float recencyWeight = 0.3f,
        float importanceWeight = 0.5f,
        float decayFactor = 0.995f)
    {
        float similarity = 0f;
        if (record.Embedding.Length > 0 && queryEmbedding.Length == record.Embedding.Length)
        {
            float rawCos = VectorMath.CosineSimilarity(queryEmbedding, record.Embedding);
            similarity = Math.Max(0f, rawCos);
        }

        DateTime referenceTime = record.LastAccessed != DateTime.MinValue ? record.LastAccessed : record.Timestamp;
        double elapsedHours = Math.Max(0.0, (nowUtc - referenceTime).TotalHours);
        float recency = (float)Math.Pow(decayFactor, elapsedHours);

        float normalizedImportance = Math.Clamp(record.Importance / 10.0f, 0.1f, 1.0f);

        float finalScore = (relevanceWeight * similarity) +
                           (recencyWeight * recency) +
                           (importanceWeight * normalizedImportance);

        return new ScoredMemory
        {
            Record = record,
            Similarity = similarity,
            RecencyScore = recency,
            ImportanceScore = normalizedImportance,
            FinalScore = finalScore
        };
    }

    /// <summary>
    /// Scores and ranks candidate memories, filtering out candidates below minSimilarityThreshold.
    /// Returns the ranked list ordered by FinalScore descending.
    /// </summary>
    public static List<ScoredMemory> ScoreAndRank(
        IEnumerable<MemoryRecord> candidates,
        ReadOnlySpan<float> queryEmbedding,
        DateTime nowUtc,
        float minSimilarityThreshold = 0.35f,
        float relevanceWeight = 1.0f,
        float recencyWeight = 0.3f,
        float importanceWeight = 0.5f,
        float decayFactor = 0.995f)
    {
        List<ScoredMemory> scored = [];

        foreach (MemoryRecord candidate in candidates)
        {
            ScoredMemory item = Score(candidate, queryEmbedding, nowUtc,
                relevanceWeight, recencyWeight, importanceWeight, decayFactor);

            if (item.Similarity >= minSimilarityThreshold)
            {
                scored.Add(item);
            }
        }

        scored.Sort((a, b) => b.FinalScore.CompareTo(a.FinalScore));
        return scored;
    }
}
