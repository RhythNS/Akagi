using Akagi.Puppeteers.Commands;
using Akagi.Puppeteers.SystemProcessors;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Puppeteers;

static class DependendyInjection
{
    public static void AddPuppeteers(this IServiceCollection services)
    {
        services.AddScoped<IPuppeteer, Puppeteer>();
        services.AddScoped<ICommandFactory, CommandFactory>();
        services.AddSingleton<ISystemProcessorDatabase, SystemProcessorDatabase>();
    }
}
