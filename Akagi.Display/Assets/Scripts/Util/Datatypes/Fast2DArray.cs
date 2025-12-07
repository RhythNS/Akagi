using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A simulated 2D array on a simple 1D array to reduce access time for elements in the arary.
/// Can throw OutOfBoundsExceptions!
/// </summary>
/// <typeparam name="T">Type of elements</typeparam>
public class Fast2DArray<T> : IEnumerable<T>
{
    private T[] array;

    public int XSize { private set; get; }
    public int YSize { private set; get; }
    public Vector2Int Size => new Vector2Int(XSize, YSize);

    public Fast2DArray(int xSize, int ySize)
    {
        XSize = xSize;
        YSize = ySize;
        array = new T[xSize * ySize];
    }

    /// <summary>
    /// Provides access to the array elements.
    /// </summary>
    /// <returns>The element at the specified position.</returns>
    public T this[int x, int y]
    {
        get
        {
            if (x >= XSize || y >= YSize || x < 0 || y < 0)
                return default;
            return array[x * YSize + y];
        }
        set
        {
            if (x >= XSize || y >= YSize || x < 0 || y < 0)
                throw new System.IndexOutOfRangeException("x or y index outside of array size.");
            array[x * YSize + y] = value;
        }
    }

    /// <summary>
    /// Returns the element at the specified position of the array.
    /// </summary>
    public T Get(int x, int y) => array[x * YSize + y];

    /// <summary>
    /// Sets the element at the specified position of the array.
    /// </summary>
    public void Set(T element, int x, int y) => array[x * YSize + y] = element;

    /// <summary>
    /// Returns this Fast2DArray as a standard array.
    /// </summary>
    public T[] AsArray
    {
        get => array;
    }

    /// <summary>
    /// Util method to check if given ints are in the bounds of the array.
    /// </summary>
    public bool InBounds(int x, int y) => x > -1 && x < XSize && y > -1 && y < YSize;

    /// <summary>
    /// Util method to check if given ints are in the bounds of the array.
    /// </summary>
    public bool InBounds(Vector2Int pos) => pos.x > -1 && pos.x < XSize && pos.y > -1 && pos.y < YSize;

    /// <summary>
    /// Resizes the array to the specified x- and y-values.
    /// </summary>
    /// <param name="xSize">The new x-size.</param>
    /// <param name="ySize">The new y-size.</param>
    public void Resize(int xSize, int ySize)
    {
        Fast2DArray<T> newArray = new Fast2DArray<T>(xSize, ySize);

        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                if (InBounds(x, y))
                {
                    newArray[x, y] = this[x, y];
                }
            }
        }

        XSize = xSize;
        YSize = ySize;

        array = newArray.AsArray;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < array.Length; i++)
            yield return array[i];
    }

    /// <summary>
    /// Get the enumerator for a single array specified by the int atX
    /// </summary>
    public IEnumerator<T> GetEnumerator(int atX)
    {
        int to = atX * XSize + YSize;
        for (int i = atX * XSize; i < to; i++)
            yield return array[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
