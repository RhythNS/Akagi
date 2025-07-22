using Akagi.Connectors.Desu;
using Akagi.Connectors.Tatoeba;
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
    }
}
