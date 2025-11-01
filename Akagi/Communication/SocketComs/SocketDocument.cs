namespace Akagi.Communication.SocketComs;

internal class SocketDocument : Document
{
    public override string Name => FileName;

    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required byte[] Data { get; init; }

    public override Task<MemoryStream?> GetStream()
    {
        return Task.FromResult<MemoryStream?>(new MemoryStream(Data));
    }
}
