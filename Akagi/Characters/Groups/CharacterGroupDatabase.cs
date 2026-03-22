using Akagi.Data;
using Akagi.Users;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Groups;

internal interface ICharacterGroupDatabase : IDatabase<CharacterGroup>
{
    public Task<List<CharacterGroup>> GetGroupsForUser(User user);
}

internal class CharacterGroupDatabase : Database<CharacterGroup>, ICharacterGroupDatabase
{
    public override string CollectionName => "characterGroups";

    public CharacterGroupDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is CharacterGroup;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((CharacterGroup)savable);

    public async Task<List<CharacterGroup>> GetGroupsForUser(User user)
    {
        FilterDefinition<CharacterGroup> filter = Builders<CharacterGroup>.Filter.Eq(nameof(CharacterGroup.UserId), user.Id);
        return await GetDocumentsByPredicateAsync(filter);
    }
}
