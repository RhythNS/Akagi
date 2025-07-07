using Akagi.Communication.Commands;
using Akagi.Communication.TelegramComs;
using Akagi.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Communication;

static class DependencyInjection
{
    public static void AddCommunications(this IServiceCollection services, IConfiguration configuration)
    {
        Type[] types = TypeUtils.GetNonAbstractTypesExtendingFrom<Command>();
        Array.ForEach(types, commandType => services.AddTransient(typeof(Command), commandType));

        services.Configure<TelegramService.Options>(configuration.GetSection("Telegram"));
        services.AddSingleton<TelegramService>();
        services.AddHostedService(provider => provider.GetRequiredService<TelegramService>());

        services.AddSingleton<ICommunicator>(provider => provider.GetRequiredService<TelegramService>());
        services.AddSingleton<ICommunicatorFactory, CommunicatorFactory>();
    }
}
