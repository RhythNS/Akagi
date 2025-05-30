using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Data;

static class DependencyInjection
{
    public static void AddData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection("MongoDB"));
        services.AddSingleton<IFileDatabase, FileDatabase>();
        services.AddTransient<IDatabaseFactory, DatabaseFactory>();

        IEnumerable<Type> databaseTypes = typeof(IDatabase).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IDatabase).IsAssignableFrom(t));
        foreach (Type? type in databaseTypes)
        {
            services.AddSingleton(typeof(IDatabase), type);
        }
    }
}
