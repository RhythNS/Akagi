using Akagi.Receivers.Commands;
using Akagi.Utils;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers;

static class DependencyInjection
{
    public static void AddReceivers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileSystemCommand.Options>(configuration.GetSection("FileSystem"));

        services.AddSingleton<IReceiver, Receiver>();
        services.AddScoped<ICommandFactory, CommandFactory>();

        Type[] commandTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Command>();
        Array.ForEach(commandTypes, commandType => services.AddTransient(commandType, commandType));
        Array.ForEach(commandTypes, BsonExtensions.Register);
    }
}
