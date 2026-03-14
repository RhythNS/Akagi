using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.Interceptors;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.TriggerPoints;
using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Characters.VoiceClips;
using Akagi.Utils;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Characters;

static class DependencyInjection
{
    public static void AddCharacter(this IServiceCollection services)
    {
        services.AddSingleton<ICharacterDatabase, CharacterDatabase>();
        services.AddSingleton<ICardDatabase, CardDatabase>();
        services.AddSingleton<IVoiceClipsDatabase, VoiceClipsDatabase>();
        services.AddSingleton<ISystemProcessorDatabase, SystemProcessorDatabase>();
        services.AddSingleton<IPuppeteerDatabase, PuppeteerDatabase>();
        services.AddSingleton<IReflectorDatabase, ReflectorDatabase>();
        services.AddSingleton<IInterceptorDatabase, InterceptorDatabase>();
        services.AddSingleton<IMessageCompilerDatabase, MessageCompilerDatabase>();
        services.AddSingleton<ITriggerPointDatabase, TriggerPointDatabase>();
        services.AddSingleton<ITriggerActionDatabase, TriggerActionDatabase>();

        RegisterDBClasses();
    }

    private static void RegisterDBClasses()
    {
        Type[] messages = TypeUtils.GetNonAbstractTypesExtendingFrom<Message>();
        Array.ForEach(messages, BsonExtensions.Register);

        Type[] puppeteerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Puppeteer>();
        Array.ForEach(puppeteerTypes, BsonExtensions.Register);

        Type[] reflectorTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Reflector>();
        Array.ForEach(reflectorTypes, BsonExtensions.Register);

        Type[] interceptorTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Interceptor>();
        Array.ForEach(interceptorTypes, BsonExtensions.Register);

        Type[] messageCompilerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<MessageCompiler>();
        Array.ForEach(messageCompilerTypes, BsonExtensions.Register);

        Type[] triggerPointTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<TriggerPoint>();
        Array.ForEach(triggerPointTypes, BsonExtensions.Register);

        Type[] triggerActionTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<TriggerAction>();
        Array.ForEach(triggerActionTypes, BsonExtensions.Register);
    }
}
