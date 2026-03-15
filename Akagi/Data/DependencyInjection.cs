using Akagi.Communication.Commands.Macros;
using Akagi.Graphs;
using Akagi.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Data;

static class DependencyInjection
{
    public static void AddData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(options =>
        {
            options.ConnectionString = configuration.GetConnectionString("MongoDB") ?? string.Empty;
        });
        services.AddSingleton<IFileDatabase, FileDatabase>();
        services.AddTransient<IDatabaseFactory, DatabaseFactory>();
        services.AddSingleton<IGraphInstanceDatabase, GraphInstanceDatabase>();
        services.AddSingleton<IMacroDatabase, MacroDatabase>();

        Type[] databaseTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<IDatabase>();
        foreach (Type? type in databaseTypes)
        {
            services.AddSingleton(typeof(IDatabase), type);
        }
    }
}
