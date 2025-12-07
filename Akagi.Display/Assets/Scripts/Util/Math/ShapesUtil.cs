using UnityEngine;

/// <summary>
/// Helper class for methods relating to shapes.
/// </summary>
public abstract class ShapesUtil
{
    /// <summary>
    /// Floors a rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to be floored.</param>
    public static void RectFloor(ref Rect rect)
    {
        rect.x = Mathf.Floor(rect.x);
        rect.y = Mathf.Floor(rect.y);
        rect.width = Mathf.Floor(rect.width);
        rect.height = Mathf.Floor(rect.height);
    }

    /// <summary>
    /// Rounds all integers of the rect to the next integer.
    /// </summary>
    /// <param name="rect"></param>
    public static void RectRoundToNextInt(ref Rect rect)
    {
        rect.x = Mathf.Round(rect.x);
        rect.y = Mathf.Round(rect.y);
        rect.width = Mathf.Round(rect.width);
        rect.height = Mathf.Round(rect.height);
    }

    /// <summary>
    /// Sets a point to the middle point of a rectangle.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="midPoint">The point to be set.</param>
    public static void SetPositionWithMidPoint(ref Rect rect, in Vector2 midPoint)
    {
        rect.x = midPoint.x - rect.width / 2;
        rect.y = midPoint.y - rect.height / 2;
    }
}
