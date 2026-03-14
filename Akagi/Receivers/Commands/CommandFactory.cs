using Akagi.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers.Commands;

internal interface ICommandFactory
{
    public T Create<T>() where T : Command;

    public Command Create(string commandName);
}

internal class CommandFactory : ICommandFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _commandsByName;

    public CommandFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        _commandsByName = TypeUtils.GetNonAbstractTypesExtendingFrom<Command>()
            .Select(t => (Command)serviceProvider.GetRequiredService(t))
            .ToDictionary(c => c.Name, c => c.GetType());
    }

    public T Create<T>() where T : Command
    {
        return (T)_serviceProvider.GetRequiredService(typeof(T));
    }

    public Command Create(string commandName)
    {
        if (_commandsByName.TryGetValue(commandName, out Type? type) == false)
        {
            throw new InvalidOperationException($"Command with name '{commandName}' could not be found.");
        }
        return (Command)_serviceProvider.GetRequiredService(type);
    }
}
