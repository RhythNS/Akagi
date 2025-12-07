using System.Collections;
using UnityEngine;

/// <summary>
/// Enumerator methods that are commonly used.
/// </summary>
public static class EnumeratorUtil
{
    /// <summary>
    /// Waits for a specified number of seconds.
    /// </summary>
    public static IEnumerator WaitForSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
    }

    public static IEnumerator AtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
    }

    /// <summary>
    /// Executes enumerators in sequence.
    /// </summary>
    public static IEnumerator Sequence(params IEnumerator[] enumerators)
    {
        for (int i = 0; i < enumerators.Length; i++)
        {
            yield return enumerators[i];
        }
    }

    /// <summary>
    /// Lerps a transform from its current position to a given position in given seconds.
    /// </summary>
    /// <param name="transform">The transform to move.</param>
    /// <param name="position">To where the transform should move to.</param>
    /// <param name="seconds">How long the moving should go on for in seconds.</param>
    public static IEnumerator GoToInSecondsLerp(Transform transform, Vector2 position, float seconds)
    {
        Vector3 oldPos = transform.position;
        Vector3 newPos = position;
        newPos.z = oldPos.z;

        float timer = 0;
        do
        {
            timer += Time.deltaTime;
            transform.position = Vector3.Lerp(oldPos, newPos, timer / seconds);
            yield return null;
        } while (timer < seconds);
    }

    /// <summary>
    /// Slerps a transform from its current position to a given position in given seconds.
    /// </summary>
    /// <param name="transform">The transform to move.</param>
    /// <param name="position">To where the transform should move to.</param>
    /// <param name="seconds">How long the moving should go on for in seconds.</param>
    public static IEnumerator GoToInSecondsSlerp(Transform transform, Vector2 position, float seconds)
    {
        Vector3 oldPos = transform.position;
        Vector3 newPos = position;
        newPos.z = oldPos.z;

        float timer = 0;
        do
        {
            timer += Time.deltaTime;
            transform.position = Vector3.Slerp(oldPos, newPos, timer / seconds);
            yield return null;
        } while (timer < seconds);
    }

    /// <summary>
    /// Moves a transform from its current position to a given position in a way that is specified
    /// by an animation curve in given seconds.
    /// </summary>
    /// <param name="transform">The transform to move.</param>
    /// <param name="position">To where the transform should move to.</param>
    /// <param name="curve">How the transform should move.</param>
    /// <param name="seconds">How long the moving should go on for in seconds.</param>
    public static IEnumerator GoToInSecondsCurve(Transform transform, Vector2 position, AnimationCurve curve, float seconds)
    {
        Vector3 oldPos = transform.position;
        Vector3 newPos = position;
        newPos.z = oldPos.z;

        float timer = 0;
        do
        {
            timer += Time.deltaTime;
            transform.position = Vector3.LerpUnclamped(oldPos, newPos, curve.Evaluate(timer / seconds));
            yield return null;
        } while (timer < seconds);
    }

    /// <summary>
    /// Moves a transform from its current local position to a given local position in a way that is 
    /// specified by an animation curve in given seconds.
    /// </summary>
    /// <param name="transform">The transform to move.</param>
    /// <param name="position">To where the transform should move to.</param>
    /// <param name="curve">How the transform should move.</param>
    /// <param name="seconds">How long the moving should go on for in seconds.</param>
    public static IEnumerator GoToInSecondsLocalCurve(Transform transform, Vector2 position, AnimationCurve curve, float seconds)
    {
        Vector3 oldPos = transform.localPosition;
        Vector3 newPos = position;
        newPos.z = oldPos.z;

        float timer = 0;
        do
        {
            timer += Time.deltaTime;
            transform.localPosition = Vector3.LerpUnclamped(oldPos, newPos, curve.Evaluate(timer / seconds));
            yield return null;
        } while (timer < seconds);
    }

    public static IEnumerator ScaleInSecondsCurve(Transform transform, Vector3 scale, AnimationCurve curve, float seconds)
    {
        Vector3 oldScale = transform.localScale;
        Vector3 newScale = scale;

        float timer = 0;
        float perc = 0.0f;
        bool timerFinished = false;
        while (timerFinished == false)
        {
            yield return null;
            timer += Time.deltaTime;
            perc = timer / seconds;
            if (perc > 1.0f)
            {
                perc = 1.0f;
                timerFinished = true;
            }
            transform.localScale = Vector3.LerpUnclamped(oldScale, newScale, curve.Evaluate(perc / seconds));
        }
    }

    /// <summary>
    /// Fades a canvas group to an alpha value specified by an animation curve in given seconds.
    /// </summary>
    /// <param name="group">The group of which to change the alpha from.</param>
    /// <param name="curve">How the alpha changes over time.</param>
    /// <param name="seconds">How long the animation should play for in seconds.</param>
    /// <param name="inverted">Wheter to reverse the animation curve.</param>
    public static IEnumerator FadeGroupCurve(CanvasGroup group, AnimationCurve curve, float seconds, bool inverted = false)
    {
        float timer = 0;
        do
        {
            timer += Time.deltaTime;
            float eval = curve.Evaluate(timer / seconds);
            group.alpha = inverted ? 1 - eval : eval;
            yield return null;
        } while (timer < seconds);
    }
}
