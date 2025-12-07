using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helper class for methods related to random.
/// </summary>
public static class RandomUtil
{
    /// <summary>
    /// Returns a random element of a given array.
    /// </summary>
    /// <typeparam name="T">The type of the array.</typeparam>
    /// <param name="array">The array to take the random element from.</param>
    /// <returns>The random element.</returns>
    public static T Element<T>(T[] array) => array[Random.Range(0, array.Length)];

    /// <summary>
    /// Returns a random element of a given list.
    /// </summary>
    /// <typeparam name="T">The type of the list.</typeparam>
    /// <param name="array">The list to take the random element from.</param>
    /// <returns>The random element.</returns>
    public static T Element<T>(List<T> array) => array[Random.Range(0, array.Count)];
}
