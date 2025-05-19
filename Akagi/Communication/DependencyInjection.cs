using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Communication;

static class DependencyInjection
{
    public static void AddCommunications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelegramService.Options>(configuration.GetSection("Telegram"));
        services.AddHostedService<TelegramService>();
    }
}
