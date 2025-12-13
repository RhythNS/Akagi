using Akagi.Characters.Cards;
using Akagi.Characters.CharacterBehaviors.MessageCompilers;
using Akagi.Characters.CharacterBehaviors.Puppeteers;
using Akagi.Characters.CharacterBehaviors.Reflectors;
using Akagi.Characters.CharacterBehaviors.SystemProcessors;
using Akagi.Characters.Conversations;
using Akagi.Characters.Presets;
using Akagi.Characters.TriggerPoints;
using Akagi.Characters.TriggerPoints.Actions;
using Akagi.Utils;
using Akagi.Utils.Extensions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;

namespace Akagi.Characters;

static class DependencyInjection
{
    public static void AddCharacter(this IServiceCollection services)
    {
        services.AddSingleton<ICharacterDatabase, CharacterDatabase>();
        services.AddSingleton<ICardDatabase, CardDatabase>();
        services.AddSingleton<ISystemProcessorDatabase, SystemProcessorDatabase>();
        services.AddSingleton<IPuppeteerDatabase, PuppeteerDatabase>();
        services.AddSingleton<IReflectorDatabase, ReflectorDatabase>();
        services.AddSingleton<IMessageCompilerDatabase, MessageCompilerDatabase>();
        services.AddSingleton<IPresetDatabase, PresetDatabase>();
        services.AddSingleton<ITriggerPointDatabase, TriggerPointDatabase>();
        services.AddSingleton<ITriggerActionDatabase, TriggerActionDatabase>();

        services.AddScoped<IPresetCreator, PresetCreator>();

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

        Type[] messageCompilerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<MessageCompiler>();
        Array.ForEach(messageCompilerTypes, BsonExtensions.Register);

        BsonClassMap.RegisterClassMap<Preset>(classMap =>
        {
            classMap.AutoMap();
            classMap.SetIsRootClass(true);
        });

        Type[] presetTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Preset>();
        Array.ForEach(presetTypes, BsonExtensions.Register);

        Type[] triggerPointTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<TriggerPoint>();
        Array.ForEach(triggerPointTypes, BsonExtensions.Register);

        Type[] triggerActionTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<TriggerAction>();
        Array.ForEach(triggerActionTypes, BsonExtensions.Register);
    }
}
