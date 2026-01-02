using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Graphs;

internal interface IGraphInstanceDatabase : IDatabase<GraphInstance>
{
    Task<GraphInstance?> GetByGraphIdAndUserId(string graphId, string userId);
}

internal class GraphInstanceDatabase : Database<GraphInstance>, IGraphInstanceDatabase
{
    public GraphInstanceDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "graph_instances")
    {
    }

    public override bool CanSave(Savable savable) => savable is GraphInstance;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((GraphInstance)savable);

    public async Task<GraphInstance?> GetByGraphIdAndUserId(string graphId, string userId)
    {
        FilterDefinition<GraphInstance> filter = Builders<GraphInstance>.Filter.And(
            Builders<GraphInstance>.Filter.Eq(g => g.GraphId, graphId),
            Builders<GraphInstance>.Filter.Eq(g => g.UserId, userId)
        );

        List<GraphInstance> results = await GetDocumentsByPredicateAsync(filter);
        return results.FirstOrDefault();
    }
}
