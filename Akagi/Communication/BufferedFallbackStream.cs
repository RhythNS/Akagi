using Akagi.Characters;
using Akagi.Users;
using System.Text;

namespace Akagi.Communication;

internal class BufferedFallbackStream : IMessageStream
{
    private readonly ICommunicator _communicator;
    private readonly User _user;
    private readonly Character _character;
    private readonly StringBuilder _buffer = new();

    public BufferedFallbackStream(ICommunicator communicator, User user, Character character)
    {
        _communicator = communicator ?? throw new ArgumentNullException(nameof(communicator));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _character = character ?? throw new ArgumentNullException(nameof(character));
    }

    public Task WriteChunkAsync(string chunk, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(chunk))
        {
            _buffer.Append(chunk);
        }
        return Task.CompletedTask;
    }

    public async Task CompleteAsync(string fullMessage, CancellationToken ct = default)
    {
        string final = !string.IsNullOrEmpty(fullMessage) ? fullMessage : _buffer.ToString();
        if (!string.IsNullOrWhiteSpace(final))
        {
            await _communicator.SendMessage(_user, _character, final);
        }
    }

    public Task AbortAsync(Exception ex, CancellationToken ct = default)
    {
        _buffer.Clear();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
