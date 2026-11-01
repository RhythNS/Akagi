using Akagi.Characters.Cards;
using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Collections.Concurrent;

namespace Akagi.Characters;

internal interface ICharacterDatabase : IDatabase<Character>
{
    public Task<Character?> GetCharacter(string id);

    public Task<List<Character>> GetCharactersForUser(User user);

    public Task<List<Character>> GetCharactersWithRoutines();
}

internal class CharacterDatabase : Database<Character>, ICharacterDatabase
{
    public override string CollectionName => "characters";

    private readonly ICardDatabase _cardDatabase;
    private readonly ILogger<CharacterDatabase> _logger;
    private readonly ConcurrentDictionary<string, byte> _reportedOrphans = new();

    public CharacterDatabase(IOptionsMonitor<DatabaseOptions> options, ICardDatabase cardDatabase, ILogger<CharacterDatabase> logger) : base(options)
    {
        _cardDatabase = cardDatabase;
        _logger = logger;
        options.OnChange(OnOptionsChange);
        OnOptionsChange(options.CurrentValue);
    }

    private void OnOptionsChange(DatabaseOptions options)
    {
        // Handle any changes to the options if necessary
    }

    public override bool CanSave(Savable savable) => savable is Character;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Character)savable);

    public async Task<Character?> GetCharacter(string id)
    {
        Character? character = await GetDocumentByIdAsync(id);
        return character != null && await TryInitCharacter(character) ? character : null;
    }

    public async Task<List<Character>> GetCharactersForUser(User user)
    {
        FilterDefinitionBuilder<Character> builder = Builders<Character>.Filter;
        FilterDefinition<Character> definition = builder.Eq(nameof(Character.UserId), user.Id);
        return await InitCharacters(await GetDocumentsByPredicateAsync(definition));
    }

    public async Task<List<Character>> GetCharactersWithRoutines()
    {
        FilterDefinition<Character> filter = Builders<Character>.Filter.SizeGt(nameof(Character.RoutineIds), 0);
        return await InitCharacters(await GetDocumentsByPredicateAsync(filter));
    }

    private async Task<List<Character>> InitCharacters(List<Character> characters)
    {
        List<Character> usable = [];
        foreach (Character character in characters)
        {
            if (await TryInitCharacter(character))
            {
                usable.Add(character);
            }
        }
        return usable;
    }

    /// <summary>
    /// A character without its card cannot run, and a failed upload can leave one behind. It must not take the others
    /// down with it: lists of characters drive routines, the website and call analysis.
    /// </summary>
    private async Task<bool> TryInitCharacter(Character character)
    {
        Card? card = await _cardDatabase.GetDocumentByIdAsync(character.CardId);
        if (card == null)
        {
            if (_reportedOrphans.TryAdd(character.Id!, 0))
            {
                _logger.LogWarning("Skipping character {CharacterId} ({Name}): card {CardId} does not exist", character.Id, character.Name, character.CardId);
            }
            return false;
        }

        character.Init(card);
        await character.AfterLoad();
        return true;
    }
}
