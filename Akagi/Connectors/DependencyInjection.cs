using Akagi.Connectors.Desu;
using Akagi.Connectors.GoogleHealth;
using Akagi.Connectors.Tatoeba;
using Akagi.Connectors.TimeTracker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Connectors;

static class DependencyInjection
{
    public static void AddConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TatoebaConnector.Options>(configuration.GetSection("Tatoeba"));
        services.AddScoped<ITatoebaConnector, TatoebaConnector>();
        services.AddScoped<IDesuConnector, DesuConnector>();

        services.Configure<TimeTrackerConnector.Options>(configuration.GetSection("TimeTracker"));
        services.AddScoped<ITimeTrackerConnector, TimeTrackerConnector>();

        services.Configure<GoogleHealthOptions>(configuration.GetSection("GoogleHealth"));
        services.AddHttpClient<IGoogleHealthClient, GoogleHealthClient>();
        services.AddSingleton<IGoogleHealthAccountDatabase, GoogleHealthAccountDatabase>();
        services.AddTransient<IGoogleHealthSync, GoogleHealthSync>();
        services.AddHostedService<GoogleHealthSyncService>();
    }
}
