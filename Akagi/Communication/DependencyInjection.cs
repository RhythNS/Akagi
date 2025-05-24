using Akagi.Communication.Commands;
using Akagi.Communication.TelegramComs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Communication;

static class DependencyInjection
{
    public static void AddCommunications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<Command, ChangeNameCommand>();
        services.AddTransient<Command, ChangeUsernameCommand>();
        services.AddTransient<Command, CreateCharacterCommand>();
        services.AddTransient<Command, ListCardsCommand>();
        services.AddTransient<Command, ListCharactersCommand>();
        services.AddTransient<Command, ListSystemProcessorsCommand>();
        services.AddTransient<Command, PingCommand>();
        services.AddTransient<Command, UploadCardCommand>();
        services.AddTransient<Command, UploadSystemProcessorCommand>();

        services.Configure<TelegramService.Options>(configuration.GetSection("Telegram"));
        services.AddHostedService<TelegramService>();
    }
}
