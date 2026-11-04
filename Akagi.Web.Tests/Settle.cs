using Bunit;
using Microsoft.AspNetCore.Components;

namespace Akagi.Web.Tests;

internal static class Settle
{
    /// <summary>
    /// Waits until a page that loads asynchronously stops rendering. Its async lifecycle method ends with one more render,
    /// and an event raised on the markup before it can land on an element that is about to be replaced.
    /// </summary>
    public static IRenderedComponent<T> Settled<T>(this IRenderedComponent<T> cut, int quietMilliseconds = 60) where T : IComponent
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        int count;
        do
        {
            count = cut.RenderCount;
            Thread.Sleep(quietMilliseconds);
        }
        while (cut.RenderCount != count && DateTime.UtcNow < deadline);
        return cut;
    }
}
