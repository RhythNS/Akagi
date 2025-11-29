using Akagi.Receivers.Commands;
using Akagi.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers;

static class DependendyInjection
{
    public static void AddPuppeteers(this IServiceCollection services)
    {
        services.AddSingleton<IReceiver, Receiver>();
        services.AddScoped<ICommandFactory, CommandFactory>();

        Type[] commandTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Command>();
        Array.ForEach(commandTypes, commandType => services.AddTransient(commandType, commandType));
    }
}
