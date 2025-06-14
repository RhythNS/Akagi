using Akagi.Utils;
using DnsClient.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Akagi.Scheduling.Tasks;

internal class CleanUpTask : DailyTask
{
    private IEnumerable<ICleanable>? _cleanables = null;
    private ILogger<CleanUpTask>? _logger = null;

    protected override Task InnerAfterLoad()
    {
        _cleanables = Globals.Instance.ServiceProvider.GetService<IEnumerable<ICleanable>>() ?? throw new InvalidOperationException("ICleanable array not registered in the service provider.");
        _logger = Globals.Instance.GetLogger<CleanUpTask>();
        return Task.CompletedTask;
    }

    protected async override Task ExecuteTaskAsync()
    {
        if (_cleanables == null)
        {
            throw new InvalidOperationException("Cleanables not initialized. Ensure InnerAfterLoad has been called.");
        }
        if (_logger == null)
        {
            throw new InvalidOperationException("Logger not initialized. Ensure InnerAfterLoad has been called.");
        }

        foreach (ICleanable cleanable in _cleanables)
        {
            try
            {
                await cleanable.CleanUpAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup for {CleanableType}", cleanable.GetType().Name);
            }
        }
    }
}
