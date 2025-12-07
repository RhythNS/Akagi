using UnityEngine;

/// <summary>
/// A 2d Circle.
/// </summary>
[System.Serializable]
public class Circle2D : MonoBehaviour
{
    public float radius;
    public Vector2 position;

    public Circle2D(float x, float y, float radius)
    {
        position = new Vector2(x, y);
        this.radius = radius;
    }

    public Circle2D(Vector2 position, float radius)
    {
        this.position = position;
        this.radius = radius;
    }

    public void SetPosition(float x, float y) => position.Set(x, y);

    public void SetPosition(Vector2 position) => this.position = position;

    public Vector2 GetPosition() => position;
}
