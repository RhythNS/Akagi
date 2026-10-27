using Akagi.Characters.CharacterBehaviors.Interceptors;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Flow;
using Akagi.Receivers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Akagi.Characters.Groups;

/// <summary>
/// Runs one character's puppeteer (with its interceptors) inside a prepared context. Shared by group strategies
/// and by the Consult tool.
/// </summary>
internal static class GroupMemberRunner
{
    public static async Task RunAsync(Character character, Context context)
    {
        ISystemProcessorDatabase systemProcessorDatabase = context.DatabaseFactory.GetDatabase<ISystemProcessorDatabase>();

        if (character.InterceptorIds.Length > 0)
        {
            IInterceptorDatabase interceptorDatabase = context.DatabaseFactory.GetDatabase<IInterceptorDatabase>();
            ILogger interceptorLogger = Globals.Instance.ServiceProvider.GetRequiredService<ILogger<Interceptor>>();
            List<Interceptor> interceptors = [];

            foreach (string id in character.InterceptorIds)
            {
                Interceptor interceptor = await interceptorDatabase.GetDocumentByIdAsync(id)
                    ?? throw new Exception($"Interceptor with ID {id} not found for character {character.Id}");

                await interceptor.Init(interceptorLogger, context, systemProcessorDatabase);
                interceptors.Add(interceptor);
            }

            context.Communicator = new InterceptingCommunicator(context.Communicator, [.. interceptors]);
        }

        IPuppeteerDatabase puppeteerDatabase = context.DatabaseFactory.GetDatabase<IPuppeteerDatabase>();
        Puppeteer puppeteer = await puppeteerDatabase.GetDocumentByIdAsync(character.PuppeteerId)
            ?? throw new Exception($"Puppeteer with ID {character.PuppeteerId} not found for character {character.Id}");

        ILogger puppeteerLogger = Globals.Instance.ServiceProvider.GetRequiredService<ILogger<Puppeteer>>();

        await puppeteer.Init(puppeteerLogger, context, systemProcessorDatabase);
        await puppeteer.ProcessAsync();
    }
}
