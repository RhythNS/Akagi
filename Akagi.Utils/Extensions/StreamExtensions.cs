namespace Akagi.Utils.Extensions;

public static class StreamExtensions
{
    public static async Task<string> ToBase64(this Stream stream)
    {
        if (stream.CanRead == false)
        {
            throw new InvalidOperationException("Stream must be readable.");
        }
        if (stream is MemoryStream castedStream)
        {
            return Convert.ToBase64String(castedStream.ToArray());
        }
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using MemoryStream memoryStream = new();
        await stream.CopyToAsync(memoryStream);
        return Convert.ToBase64String(memoryStream.ToArray());
    }
}
