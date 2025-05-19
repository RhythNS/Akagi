using Akagi.Data;

namespace Akagi.Characters;

internal interface ICharacterDatabase : IDatabase<Character>
{
    public Task<Character> GetCharacter(string id);
}
