using Akagi.Flow;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Scheduling.Tasks;

internal class DeferTask : OneShotTask
{
    public interface IDeferHandler
    {
        public Task OnDeferAsync(object? data);
    }

    private Type? _handlerType;
    private object? _data;

    public Type? HandlerType
    {
        get => _handlerType;
        set => SetProperty(ref _handlerType, value);
    }
    public object? Data
    {
        get => _data;
        set => SetProperty(ref _data, value);
    }

    protected override async Task ExecuteTaskAsync()
    {
        if (HandlerType == null)
        {
            throw new InvalidOperationException("HandlerType must be set before executing the task.");
        }
        IDeferHandler handler = (IDeferHandler)Globals.Instance.ServiceProvider.GetRequiredService(HandlerType);
        await handler.OnDeferAsync(Data);
    }
}
