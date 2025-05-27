using Akagi.Characters.Conversations;
using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Receivers.Puppeteers;
using Akagi.Receivers.SystemProcessors;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;

namespace Akagi.Receivers;

static class DependendyInjection
{
    public static void AddPuppeteers(this IServiceCollection services)
    {
        services.AddScoped<IReceiver, Receiver>();
        services.AddScoped<ICommandFactory, CommandFactory>();
        services.AddScoped<TextMessageCommand>();
        services.AddSingleton<ISystemProcessorDatabase, SystemProcessorDatabase>();
        services.AddSingleton<IPuppeteerDatabase, PuppeteerDatabase>();
        RegisterDBClasses();
    }

    private static void RegisterDBClasses()
    {
        BsonClassMap.RegisterClassMap<SinglePuppeteer>();
    }
}
