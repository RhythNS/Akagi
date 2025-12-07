using UnityEngine;

/// <summary>
/// Helper class for gizmos related methods.
/// </summary>
public static class GizmosUtil
{
    /// <summary>
    /// Draws a point.
    /// </summary>
    /// <param name="point">The position of the point.</param>
    /// <param name="length">The length of the lines that mark the point.</param>
    public static void DrawPoint(Vector2 point, float length = 0.125f)
    {
        Vector3 position = point;
        Gizmos.DrawLine(position + new Vector3(-length, -length), position + new Vector3(length, length));
        Gizmos.DrawLine(position + new Vector3(-length, length), position + new Vector3(length, -length));
    }
}
