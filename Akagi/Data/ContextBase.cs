namespace Akagi.Data;

internal abstract class ContextBase : IAsyncDisposable
{
    protected abstract Savable[] ToTrack { get; }

    public required IDatabaseFactory DatabaseFactory { get; init; }

    public async ValueTask DisposeAsync()
    {
        foreach (Savable savable in ToTrack)
        {
            // TODO: uncomment if properly implemented
            // if (savable.Dirty)
            await DatabaseFactory.TrySave(savable);
        }
    }
}
