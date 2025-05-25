using Akagi.Characters;
using Akagi.Communication;
using Akagi.Data;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Users;
using Microsoft.Extensions.Hosting;

namespace Akagi;

internal class Program
{
    static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        /*
        builder.Services.AddLogging(loggingBuilder =>
        {
            loggingBuilder
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Akagi", LogLevel.Debug)
                .AddConsole();
        });
         */

        builder.Services.AddData(builder.Configuration);
        builder.Services.AddPuppeteers();
        builder.Services.AddCharacter();
        builder.Services.AddUsers();
        builder.Services.AddLLMs();
        builder.Services.AddCommunications(builder.Configuration);

        using IHost host = builder.Build();
        await host.RunAsync();
    }
}
