using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Puppeteers.Commands;

internal class CommandFactory : ICommandFactory
{
    private readonly IServiceProvider _serviceProvider;

    public CommandFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T Create<T>() where T : Command
    {
        T command = (T)_serviceProvider.GetRequiredService(typeof(T));
        if (command == null)
        {
            throw new InvalidOperationException($"Command of type {typeof(T)} could not be created.");
        }
        return command;
    }
}
