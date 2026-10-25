using System.Numerics;

namespace Akagi.Utils;

public static class VectorMath
{
    /// <summary>
    /// Computes the cosine similarity between two float vectors using SIMD acceleration.
    /// Returns a value between -1.0 (opposite) and 1.0 (identical), or 0.0 if either vector has zero magnitude.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException($"Vectors must have identical dimensions. Got {vectorA.Length} and {vectorB.Length}.");
        }

        if (vectorA.IsEmpty)
        {
            return 0f;
        }

        int length = vectorA.Length;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        Vector<float> dotVector = Vector<float>.Zero;
        Vector<float> normASqVector = Vector<float>.Zero;
        Vector<float> normBSqVector = Vector<float>.Zero;

        for (; i <= length - vectorSize; i += vectorSize)
        {
            Vector<float> va = new(vectorA.Slice(i, vectorSize));
            Vector<float> vb = new(vectorB.Slice(i, vectorSize));

            dotVector += va * vb;
            normASqVector += va * va;
            normBSqVector += vb * vb;
        }

        float dot = Vector.Dot(dotVector, Vector<float>.One);
        float normASq = Vector.Dot(normASqVector, Vector<float>.One);
        float normBSq = Vector.Dot(normBSqVector, Vector<float>.One);

        // Process remaining elements when length is not a multiple of vectorSize
        for (; i < length; i++)
        {
            float a = vectorA[i];
            float b = vectorB[i];

            dot += a * b;
            normASq += a * a;
            normBSq += b * b;
        }

        if (normASq <= 0f || normBSq <= 0f)
        {
            return 0f;
        }

        float similarity = dot / (MathF.Sqrt(normASq) * MathF.Sqrt(normBSq));
        return Math.Clamp(similarity, -1f, 1f);
    }

    /// <summary>
    /// Computes the dot product of two float vectors using SIMD acceleration.
    /// </summary>
    public static float DotProduct(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException($"Vectors must have identical dimensions. Got {vectorA.Length} and {vectorB.Length}.");
        }

        int length = vectorA.Length;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        Vector<float> dotVector = Vector<float>.Zero;

        for (; i <= length - vectorSize; i += vectorSize)
        {
            Vector<float> va = new(vectorA.Slice(i, vectorSize));
            Vector<float> vb = new(vectorB.Slice(i, vectorSize));
            dotVector += va * vb;
        }

        float dot = Vector.Dot(dotVector, Vector<float>.One);

        for (; i < length; i++)
        {
            dot += vectorA[i] * vectorB[i];
        }

        return dot;
    }

    /// <summary>
    /// Computes the Euclidean distance between two float vectors using SIMD acceleration.
    /// </summary>
    public static float EuclideanDistance(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException($"Vectors must have identical dimensions. Got {vectorA.Length} and {vectorB.Length}.");
        }

        int length = vectorA.Length;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        Vector<float> diffSqVector = Vector<float>.Zero;

        for (; i <= length - vectorSize; i += vectorSize)
        {
            Vector<float> diff = new Vector<float>(vectorA.Slice(i, vectorSize)) - new Vector<float>(vectorB.Slice(i, vectorSize));
            diffSqVector += diff * diff;
        }

        float sumDiffSq = Vector.Dot(diffSqVector, Vector<float>.One);

        for (; i < length; i++)
        {
            float diff = vectorA[i] - vectorB[i];
            sumDiffSq += diff * diff;
        }

        return MathF.Sqrt(sumDiffSq);
    }

    /// <summary>
    /// Normalizes a float vector in-place so its Euclidean norm equals 1.0.
    /// </summary>
    public static void Normalize(Span<float> vector)
    {
        float dot = DotProduct(vector, vector);
        if (dot <= 0f)
        {
            return;
        }

        float invNorm = 1.0f / MathF.Sqrt(dot);
        int length = vector.Length;
        int vectorSize = Vector<float>.Count;
        int i = 0;

        Vector<float> invNormVec = new(invNorm);

        for (; i <= length - vectorSize; i += vectorSize)
        {
            Vector<float> v = new Vector<float>(vector.Slice(i, vectorSize)) * invNormVec;
            v.CopyTo(vector.Slice(i, vectorSize));
        }

        for (; i < length; i++)
        {
            vector[i] *= invNorm;
        }
    }
}
