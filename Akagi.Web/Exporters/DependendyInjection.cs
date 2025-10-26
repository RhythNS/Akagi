namespace Akagi.Web.Exporters;

static class DependendyInjection
{
    public static void AddExporters(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MySqlExporterOptions>(configuration.GetSection("MySqlExporter"));
        services.AddTransient<IMySqlExporter, MySqlExporter>();
    }
}
