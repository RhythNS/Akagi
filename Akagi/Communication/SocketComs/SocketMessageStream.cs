using Akagi.Bridge.Chat.Transmissions.Responses;
using Akagi.Characters;
using Akagi.Users;
using System.Text;

namespace Akagi.Communication.SocketComs;

internal class SocketMessageStream : IMessageStream
{
    private readonly SocketService _socketService;
    private readonly User _user;
    private readonly Character _character;
    private readonly string _streamId;
    private readonly StringBuilder _accumulated = new();

    public SocketMessageStream(SocketService socketService, User user, Character character, string streamId)
    {
        _socketService = socketService ?? throw new ArgumentNullException(nameof(socketService));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _streamId = streamId;
    }

    public Task WriteChunkAsync(string chunk, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return Task.CompletedTask;
        }

        _accumulated.Append(chunk);

        StreamTextMessageResponseTransmission transmission = new()
        {
            StreamId = _streamId,
            CharacterId = _character.Id,
            TextDelta = chunk,
            IsComplete = false,
            FullText = null
        };

        _socketService.SendTransmission(_user, transmission);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(string fullMessage, CancellationToken ct = default)
    {
        string final = !string.IsNullOrEmpty(fullMessage) ? fullMessage : _accumulated.ToString();

        if (_accumulated.Length == 0 && string.IsNullOrWhiteSpace(final))
        {
            return Task.CompletedTask;
        }

        StreamTextMessageResponseTransmission transmission = new()
        {
            StreamId = _streamId,
            CharacterId = _character.Id,
            TextDelta = string.Empty,
            IsComplete = true,
            FullText = final
        };

        _socketService.SendTransmission(_user, transmission);
        return Task.CompletedTask;
    }

    public Task AbortAsync(Exception ex, CancellationToken ct = default)
    {
        if (_accumulated.Length == 0)
        {
            return Task.CompletedTask;
        }

        StreamTextMessageResponseTransmission transmission = new()
        {
            StreamId = _streamId,
            CharacterId = _character.Id,
            TextDelta = string.Empty,
            IsComplete = true,
            FullText = _accumulated.ToString()
        };

        _socketService.SendTransmission(_user, transmission);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
