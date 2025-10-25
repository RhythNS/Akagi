using Akagi.Characters;
using Akagi.Communication;
using Akagi.Connectors;
using Akagi.Data;
using Akagi.Flow;
using Akagi.LLMs;
using Akagi.Receivers;
using Akagi.Scheduling;
using Akagi.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Akagi;

internal class Program
{
    static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddUtils();
        builder.Services.AddConnectors(builder.Configuration);
        builder.Services.AddData(builder.Configuration);
        builder.Services.AddPuppeteers();
        builder.Services.AddCharacter();
        builder.Services.AddUsers();
        builder.Services.AddLLMs();
        builder.Services.AddCommunications(builder.Configuration);
        builder.Services.AddScheduling();

        using IHost host = builder.Build();

        Globals globals = host.Services.GetRequiredService<Globals>();
        await globals.Initialize();

        await host.RunAsync();
    }
}
