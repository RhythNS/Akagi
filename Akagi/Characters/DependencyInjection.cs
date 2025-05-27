using Akagi.Characters.Cards;
using Akagi.Characters.Conversations;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;

namespace Akagi.Characters;

static class DependencyInjection
{
    public static void AddCharacter(this IServiceCollection services)
    {
        services.AddSingleton<ICharacterDatabase, CharacterDatabase>();
        services.AddSingleton<ICardDatabase, CardDatabase>();
        RegisterDBClasses();
    }

    private static void RegisterDBClasses()
    {
        BsonClassMap.RegisterClassMap<TextMessage>();
        BsonClassMap.RegisterClassMap<CommandMessage>();
    }
}
