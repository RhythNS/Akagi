using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Debugging;

namespace Akagi.Logging.Extensions;

public static class AkagiLogging
{
    public static ILogger CreateDefaultLogger(IConfiguration configuration)
    {
        string appName = configuration["Logging:ApplicationName"] ?? configuration["Logging:ApplicationName:Value"] ?? "Unknown";
        string environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        return new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", appName)
            .Enrich.WithProperty("Environment", environment)
            .CreateLogger();
    }

    public static void AddDefaultLogger(this IServiceCollection services, IConfiguration configuration)
    {
        SelfLog.Enable(msg =>
        {
            Console.WriteLine($"[Serilog SelfLog] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {msg}");
        });

        Log.Logger = CreateDefaultLogger(configuration);
        services.AddSerilog();

        Log.Information("Serilog initialized");
    }
}
