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
        BsonClassMap.RegisterClassMap<TextMessage>();
        BsonClassMap.RegisterClassMap<CommandMessage>();

        Type[] puppeteerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Puppeteer>();
        Array.ForEach(puppeteerTypes, Register);

        Type[] reflectorTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Reflector>();
        Array.ForEach(reflectorTypes, Register);

        Type[] messageCompilerTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<MessageCompiler>();
        Array.ForEach(messageCompilerTypes, Register);

        BsonClassMap.RegisterClassMap<Preset>(classMap =>
        {
            classMap.AutoMap();
            classMap.SetIsRootClass(true);
        });

        Type[] presetTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<Preset>();
        Array.ForEach(presetTypes, Register);

        Type[] triggerPointTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<TriggerPoint>();
        Array.ForEach(triggerPointTypes, Register);

        Type[] triggerActionTypes = TypeUtils.GetNonAbstractTypesExtendingFrom<TriggerAction>();
        Array.ForEach(triggerActionTypes, Register);
    }

    private static void Register(Type type)
    {
        if (!BsonClassMap.IsClassMapRegistered(type))
        {
            BsonClassMap classMap = new(type);
            classMap.AutoMap();
            BsonClassMap.RegisterClassMap(classMap);
        }
    }
}
