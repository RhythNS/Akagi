using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Unity Coroutine with extended functionality.
/// </summary>
public class ExtendedCoroutine
{
    /// <summary>
    /// Wheter the coroutine has finihsed.
    /// </summary>
    public bool IsFinshed { get; private set; } = false;
    /// <summary>
    /// A reference to the Unity Coroutine.
    /// </summary>
    public Coroutine Coroutine { get; private set; }

    private readonly IEnumerator enumerator;
    private readonly Action onFinished;
    private readonly MonoBehaviour onScript;

    /// <summary>
    /// Standard constructor.
    /// </summary>
    /// <param name="onScript">The MonoBehaviour script that executes the coroutine.</param>
    /// <param name="enumerator">The content of the coroutine.</param>
    /// <param name="onFinished">Callback when the coroutine finished.</param>
    /// <param name="startNow">Wheter the coroutine should start now.</param>
    public ExtendedCoroutine(MonoBehaviour onScript, IEnumerator enumerator, Action onFinished = null, bool startNow = true)
    {
        this.onScript = onScript;
        this.enumerator = enumerator;
        this.onFinished = onFinished;

        if (startNow)
            Start();
    }

    /// <summary>
    /// Starts the coroutine.
    /// </summary>
    public void Start()
    {
        Coroutine = onScript.StartCoroutine(InnerEnumerator());
    }

    private IEnumerator InnerEnumerator()
    {
        while (enumerator.MoveNext())
        {
            yield return enumerator.Current;
        }
        IsFinshed = true;
        if (onFinished != null)
            onFinished.Invoke();
    }

    /// <summary>
    /// Stops the coroutine.
    /// </summary>
    /// <param name="invokeOnFinished">Wheter to invoke the onFinished callback.</param>
    public void Stop(bool invokeOnFinished = true)
    {
        if (Coroutine != null)
            onScript.StopCoroutine(Coroutine);
        if (invokeOnFinished && onFinished != null)
            onFinished.Invoke();
    }

    /// <summary>
    /// Helper method for executing something in given seconds.
    /// </summary>
    /// <param name="onScript">The MonoBehaviour script that executes the coroutine.</param>
    /// <param name="seconds">The amount of waiting time.</param>
    /// <param name="onFinished">What should be executed after the coroutine finished.</param>
    /// <param name="startNow">Wheter the coroutine should start now.</param>
    public static ExtendedCoroutine ActionAfterSeconds(MonoBehaviour onScript, float seconds, Action onFinished)
        => new ExtendedCoroutine(onScript, EnumeratorUtil.WaitForSeconds(seconds), onFinished, true);

    public static ExtendedCoroutine ActionAtEndOfFrame(MonoBehaviour onScript, Action onFinished)
        => new ExtendedCoroutine(onScript, EnumeratorUtil.AtEndOfFrame(), onFinished, true);
}
