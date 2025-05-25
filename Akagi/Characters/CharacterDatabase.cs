using Akagi.Characters.Cards;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters;

internal class CharacterDatabase : Database<Character>, ICharacterDatabase
{
    private readonly ICardDatabase _cardDatabase;

    public CharacterDatabase(IOptionsMonitor<DatabaseOptions> options, ICardDatabase cardDatabase) : base(options, "characters")
    {
        _cardDatabase = cardDatabase;
        options.OnChange(OnOptionsChange);
        OnOptionsChange(options.CurrentValue);
    }

    private void OnOptionsChange(DatabaseOptions options)
    {
        // Handle any changes to the options if necessary
    }

    public async Task<Character> GetCharacter(string id)
    {
        Character? character = await GetDocumentByIdAsync(id) ?? throw new Exception($"Character with ID {id} not found.");
        await InitCharacter(character);
        return character;
    }

    public async Task<List<Character>> GetCharactersForUser(User user)
    {
        FilterDefinitionBuilder<Character> builder = Builders<Character>.Filter;
        FilterDefinition<Character> definition = builder.Eq(nameof(Character.UserId), user.Id);
        List<Character> characters = await GetDocumentsByPredicateAsync(definition);

        foreach (Character character in characters)
        {
            await InitCharacter(character);
        }

        return characters;
    }

    private async Task InitCharacter(Character character)
    {
        Card? card = await _cardDatabase.GetDocumentByIdAsync(character.CardId) ?? throw new Exception($"Card with ID {character.CardId} not found.");
        character.Init(card);
    }
}
