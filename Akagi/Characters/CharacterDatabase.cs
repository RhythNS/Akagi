using Akagi.Characters.Cards;
using Akagi.Data;
using Microsoft.Extensions.Options;

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
        Character? character = await GetDocumentByIdAsync(id);
        if (character == null)
        {
            throw new Exception($"Character with ID {id} not found.");
        }
        Card? card = await _cardDatabase.GetDocumentByIdAsync(character.CardId);
        if (card == null)
        {
            throw new Exception($"Card with ID {character.CardId} not found.");
        }
        character.Card = card;
        return character;
    }
}
