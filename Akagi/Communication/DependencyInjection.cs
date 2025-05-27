using Akagi.Communication.Commands;
using Akagi.Communication.TelegramComs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Communication;

static class DependencyInjection
{
    public static void AddCommunications(this IServiceCollection services, IConfiguration configuration)
    {
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(Command).IsAssignableFrom(type) && !type.IsAbstract)
            .ToList()
            .ForEach(commandType => services.AddTransient(typeof(Command), commandType));

        services.Configure<TelegramService.Options>(configuration.GetSection("Telegram"));
        services.AddHostedService<TelegramService>();
    }
}
