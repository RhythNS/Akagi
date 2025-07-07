using Akagi.Receivers.Commands;
using Akagi.Receivers.Puppeteers;
using Akagi.Receivers.SystemProcessors;
using Akagi.Utils;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;

namespace Akagi.Receivers;

static class DependendyInjection
{
    public static void AddPuppeteers(this IServiceCollection services)
    {
        services.AddScoped<IReceiver, Receiver>();
        services.AddScoped<ICommandFactory, CommandFactory>();

        Type[] commandTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Command>();
        Array.ForEach(commandTypes, commandType => services.AddTransient(commandType, commandType));

        services.AddSingleton<ISystemProcessorDatabase, SystemProcessorDatabase>();
        services.AddSingleton<IPuppeteerDatabase, PuppeteerDatabase>();
        RegisterDBClasses();
    }

    private static void RegisterDBClasses()
    {
        BsonClassMap.RegisterClassMap<SinglePuppeteer>();
        BsonClassMap.RegisterClassMap<LinePuppeteer>();
    }
}
