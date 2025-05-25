using Akagi.Receivers.Commands;
using Akagi.Receivers.Commands.Messages;
using Akagi.Receivers.SystemProcessors;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Receivers;

static class DependendyInjection
{
    public static void AddPuppeteers(this IServiceCollection services)
    {
        services.AddScoped<IReceiver, Receiver>();
        services.AddScoped<ICommandFactory, CommandFactory>();
        services.AddScoped<TextMessageCommand>();
        services.AddSingleton<ISystemProcessorDatabase, SystemProcessorDatabase>();
    }
}
