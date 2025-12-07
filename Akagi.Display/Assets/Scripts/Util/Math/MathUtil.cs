using UnityEngine;

/// <summary>
/// Helper class related to math methods.
/// </summary>
public abstract class MathUtil
{
    public static bool InRangeInclusive(float min, float max, float val)
        => val >= min && val <= max;

    public static bool InRangeInclusive(int min, int max, int val)
        => val >= min && val <= max;

    public static bool InRange(float min, float max, float val)
        => val > min && val < max;

    public static bool InRange(int min, int max, int val)
        => val > min && val < max;

    public static float Normalize(float value, float min, float max)
        => min == 0 ? (value / max) : (value - min) / (max - min);

    public static Vector3 RandomVector3(Vector3 min, Vector3 max)
        => new Vector3(Random.Range(min.x, max.x), Random.Range(min.y, max.y), Random.Range(min.z, max.z));

    public static Vector2 RandomVector2(Vector2 min, Vector2 max)
        => new Vector2(Random.Range(min.x, max.x), Random.Range(min.y, max.y));

    public static Vector2 VectorClamp(Vector2 value, Vector2 min, Vector2 max)
    {
        value.x = Mathf.Clamp(value.x, min.x, max.x);
        value.y = Mathf.Clamp(value.y, min.y, max.y);
        return value;
    }

    /// <summary>
    /// Clamps vector and returns the magnitude. If the vector should only be clamped use Vector.ClampMagnitude instead.
    /// Taken from:
    /// https://github.com/Unity-Technologies/UnityCsReference/blob/master/Runtime/Export/Math/Vector3.cs
    /// </summary>
    /// <param name="vector">The vector to be clamped.</param>
    /// <param name="maxLength">The max length/ magnitude the vector can have.</param>
    /// <param name="magnitude">The magnitude of the vector.</param>
    /// <returns></returns>
    public static Vector2 ClampMagnitude(Vector2 vector, float maxLength, out float magnitude)
    {
        magnitude = vector.magnitude;
        if (magnitude > maxLength)
        {
            //these intermediate variables force the intermediate result to be
            //of float precision. without this, the intermediate result can be of higher
            //precision, which changes behavior.
            float normalized_x = vector.x / magnitude;
            float normalized_y = vector.y / magnitude;
            magnitude = maxLength;
            return new Vector2(normalized_x * maxLength,
                normalized_y * maxLength);
        }
        return vector;
    }
}
