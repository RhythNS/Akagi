using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Groups.Checkpoints;

internal interface IGroupCheckpointDatabase : IDatabase<GroupCheckpoint>
{
    public Task<GroupCheckpoint[]> GetCheckpointsForGroupAsync(string groupId);
    public Task<GroupCheckpoint?> GetLatestCheckpointForGroupAsync(string groupId);
}

internal class GroupCheckpointDatabase : Database<GroupCheckpoint>, IGroupCheckpointDatabase
{
    public override string CollectionName => "groupCheckpoints";

    public GroupCheckpointDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is GroupCheckpoint;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((GroupCheckpoint)savable);

    public async Task<GroupCheckpoint[]> GetCheckpointsForGroupAsync(string groupId)
    {
        FilterDefinition<GroupCheckpoint> filter = Builders<GroupCheckpoint>.Filter.Eq(c => c.GroupId, groupId);
        List<GroupCheckpoint> checkpoints = await GetDocumentsByPredicateAsync(filter);
        return [.. checkpoints];
    }

    public async Task<GroupCheckpoint?> GetLatestCheckpointForGroupAsync(string groupId)
    {
        FilterDefinition<GroupCheckpoint> filter = Builders<GroupCheckpoint>.Filter.Eq(c => c.GroupId, groupId);
        SortDefinition<GroupCheckpoint> sort = Builders<GroupCheckpoint>.Sort.Descending(c => c.CreatedOn);
        List<GroupCheckpoint> results = await GetCollection()
            .Find(filter)
            .Sort(sort)
            .Limit(1)
            .ToListAsync();
        return results.FirstOrDefault();
    }
}
