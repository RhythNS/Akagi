using Akagi.Bridge.Chat.Transmissions;

namespace Akagi.Web.Services.Sockets.Requests;

public interface IRequest;

public abstract class Request<R, T> : IRequest where T : Transmission
{
    private TaskCompletionSource<R> _taskCompletionSource = new();
    private bool _isCompleted;
    private SocketClient? _client;

    public async Task<R> Get(SocketClient client, TimeSpan? timeout = null)
    {
        _client = client;
        _client.AddRequest(this);

        _taskCompletionSource = new TaskCompletionSource<R>();
        _isCompleted = false;

        T transmission = GetTransmission();
        client.SendTransmission(transmission);

        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5);
        using CancellationTokenSource cts = new();
        Task timeoutTask = Task.Delay(effectiveTimeout, cts.Token);

        Task completedTask = await Task.WhenAny(_taskCompletionSource.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            _isCompleted = true;
            _taskCompletionSource.TrySetCanceled();
            throw new TimeoutException("Request timed out");
        }

        cts.Cancel();
        return await _taskCompletionSource.Task;
    }

    public void Fulfill(R response)
    {
        if (_isCompleted)
            return;

        _client?.RemoveRequest(this);
        _isCompleted = true;
        _taskCompletionSource.TrySetResult(response);
    }

    protected abstract T GetTransmission();
}
