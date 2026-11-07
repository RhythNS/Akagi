using Microsoft.JSInterop;

namespace Akagi.Web.Services;

/// <summary>
/// Hands bytes the bot sent over to the browser as a download.
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
}
