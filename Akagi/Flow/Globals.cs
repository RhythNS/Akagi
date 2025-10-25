using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Akagi.Flow;

internal class Globals
{
    private static Globals? _instance = null;
    public static Globals Instance => _instance
        ?? throw new InvalidOperationException("Globals has not been initialized. Ensure that it is registered in the service provider.");

    public IServiceProvider ServiceProvider { get; private set; }

    private readonly ISystemInitializer[] _systemInitializers;

    public Globals(IServiceProvider serviceProvider, IEnumerable<ISystemInitializer> systemInitializers)
    {
        ServiceProvider = serviceProvider;
        _instance = this;

        _systemInitializers = [.. systemInitializers];
    }

    public Task Initialize()
    {
        return Task.WhenAll(_systemInitializers.Select(initializer => initializer.InitializeAsync()));
    }

    public ILogger<T> GetLogger<T>() where T : class
    {
        return ServiceProvider.GetRequiredService<ILogger<T>>();
    }
}
