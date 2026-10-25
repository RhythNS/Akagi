using Akagi.Characters;
using Akagi.Users;

namespace Akagi.Communication;

internal interface IMessageStream : IAsyncDisposable
{
    public Task WriteChunkAsync(string chunk, CancellationToken ct = default);

    public Task CompleteAsync(string fullMessage, CancellationToken ct = default);

    public Task AbortAsync(Exception ex, CancellationToken ct = default);
}

internal interface IStreamingCommunicator : ICommunicator
{
    public Task<IMessageStream> OpenMessageStreamAsync(User user, Character character, CancellationToken ct = default);
}
