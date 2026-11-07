using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Akagi.Web.Services;

/// <summary>
/// Hands bytes the bot sent over to the browser, as a download or as audio to play.
/// </summary>
public static class FileDownloads
{
    public static async Task SaveAsync(IJSRuntime js, string fileName, byte[] data)
    {
        await using IJSObjectReference module = await js.InvokeAsync<IJSObjectReference>("import", "./js/files.js");
        using MemoryStream stream = new(data);
        using DotNetStreamReference reference = new(stream);
        await module.InvokeVoidAsync("downloadStream", fileName, reference);
    }

    /// <summary>
    /// Makes an &lt;audio&gt; element play mp3 bytes.
    /// </summary>
    public static async Task SetAudioAsync(IJSRuntime js, ElementReference audio, byte[] data)
    {
        await using IJSObjectReference module = await js.InvokeAsync<IJSObjectReference>("import", "./js/files.js");
        using MemoryStream stream = new(data);
        using DotNetStreamReference reference = new(stream);
        await module.InvokeVoidAsync("setAudio", audio, reference);
    }
}
