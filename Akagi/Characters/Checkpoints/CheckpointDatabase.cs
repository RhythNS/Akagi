using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Checkpoints;

internal interface ICheckpointDatabase : IDatabase<Checkpoint>
{
    public Task<Checkpoint[]> GetCheckpointsForCharacterAsync(string characterId);
    public Task<Checkpoint?> GetLatestCheckpointForCharacterAsync(string characterId);
}

internal class CheckpointDatabase : Database<Checkpoint>, ICheckpointDatabase
{
    public override string CollectionName => "checkpoints";

    public CheckpointDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Checkpoint;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Checkpoint)savable);

    public async Task<Checkpoint[]> GetCheckpointsForCharacterAsync(string characterId)
    {
        FilterDefinition<Checkpoint> filter = Builders<Checkpoint>.Filter.Eq(c => c.CharacterId, characterId);
        List<Checkpoint> checkpoints = await GetDocumentsByPredicateAsync(filter);
        return [.. checkpoints];
    }

    public async Task<Checkpoint?> GetLatestCheckpointForCharacterAsync(string characterId)
    {
        FilterDefinition<Checkpoint> filter = Builders<Checkpoint>.Filter.Eq(c => c.CharacterId, characterId);
        SortDefinition<Checkpoint> sort = Builders<Checkpoint>.Sort.Descending(c => c.CreatedOn);
        List<Checkpoint> results = await GetCollection()
            .Find(filter)
            .Sort(sort)
            .Limit(1)
            .ToListAsync();
        return results.FirstOrDefault();
    }
}
