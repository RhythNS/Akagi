using UnityEngine;

/// <summary>
/// A 2D box.
/// </summary>
public class Box2D
{
    public float x, y, width, height;

    public Box2D(float x, float y, float width, float height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public Box2D(Vector2 position, Vector2 size) : this(position.x, position.y, size.x, size.y)
    {
    }

    public Box2D()
    {
    }

    public bool Intersecting(Box2D other) =>
        x < other.x + other.width && x + width > other.x && y > other.y + height && y + height < other.y;

}
