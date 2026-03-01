using Akagi.Web.Data;

namespace Akagi.Web.Exporters;

static class DependendyInjection
{
    public static void AddExporters(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MySqlExporterOptions>(options =>
        {
            options.ConnectionString = configuration.GetConnectionString("MySQL") ?? string.Empty;
        });
        services.AddTransient<IMySqlExporter, MySqlExporter>();
    }
}
