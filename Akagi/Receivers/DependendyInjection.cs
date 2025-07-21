using Akagi.Receivers.Commands;
using Akagi.Receivers.MessageCompilers;
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
        services.AddSingleton<IMessageCompilerDatabase, MessageCompilerDatabase>();

        Type[] puppeteerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Puppeteer>();
        Array.ForEach(puppeteerTypes, puppeteerType => Register(services, puppeteerType));

        Type[] messageCompilerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<MessageCompiler>();
        Array.ForEach(messageCompilerTypes, messageCompilerType => Register(services, messageCompilerType));
    }

    private static void Register(IServiceCollection services, Type type)
    {
        if (!BsonClassMap.IsClassMapRegistered(type))
        {
            BsonClassMap classMap = new(type);
            classMap.AutoMap();
            BsonClassMap.RegisterClassMap(classMap);
        }
    }
}
