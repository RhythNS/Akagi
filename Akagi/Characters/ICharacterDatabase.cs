using Akagi.Data;
using Akagi.Users;

namespace Akagi.Characters;

internal interface ICharacterDatabase : IDatabase<Character>
{
    public Task<Character?> GetCharacter(string id);

    public Task<List<Character>> GetCharactersForUser(User user);
}
