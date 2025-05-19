using Akagi.Characters.Cards;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Characters;

static class DependencyInjection
{
    public static void AddCharacter(this IServiceCollection services)
    {
        services.AddSingleton<ICharacterDatabase, CharacterDatabase>();
        services.AddSingleton<ICardDatabase, CardDatabase>();
    }
}
