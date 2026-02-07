using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Graphs;

internal interface IGraphInstanceDatabase : IDatabase<GraphInstance>
{
    public Task<GraphInstance[]> GetGraphs(string userId);
    public Task<GraphInstance[]> GetGraphs(string graphId, string userId);
    public Task<GraphInstance?> GetGraph(string graphId, string name, string userId);
    public Task<bool> Exists(string graphId, string userId);
    public Task<bool> Exists(string graphId, string name, string userId);
}

internal class GraphInstanceDatabase : Database<GraphInstance>, IGraphInstanceDatabase
{
    public override string CollectionName => "graph_instances";

    public GraphInstanceDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is GraphInstance;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((GraphInstance)savable);

    public async Task<GraphInstance[]> GetGraphs(string userId)
    {
        FilterDefinition<GraphInstance> filter = Builders<GraphInstance>.Filter.Eq(g => g.UserId, userId);
        List<GraphInstance> instances = await GetDocumentsByPredicateAsync(filter);
        return [.. instances];
    }

    public async Task<GraphInstance[]> GetGraphs(string graphId, string userId)
    {
        FilterDefinition<GraphInstance> filter = Builders<GraphInstance>.Filter.And(
            Builders<GraphInstance>.Filter.Eq(g => g.GraphId, graphId),
            Builders<GraphInstance>.Filter.Eq(g => g.UserId, userId)
        );

        List<GraphInstance> results = await GetDocumentsByPredicateAsync(filter);
        return [.. results];
    }

    public async Task<GraphInstance?> GetGraph(string graphId, string name, string userId)
    {
        FilterDefinition<GraphInstance> filter = Builders<GraphInstance>.Filter.And(
            Builders<GraphInstance>.Filter.Eq(g => g.GraphId, graphId),
            Builders<GraphInstance>.Filter.Eq(g => g.Name, name),
            Builders<GraphInstance>.Filter.Eq(g => g.UserId, userId)
        );
        List<GraphInstance> results = await GetDocumentsByPredicateAsync(filter);
        return results.FirstOrDefault();
    }

    public async Task<bool> Exists(string graphId, string userId)
    {
        FilterDefinition<GraphInstance> filter = Builders<GraphInstance>.Filter.And(
            Builders<GraphInstance>.Filter.Eq(g => g.GraphId, graphId),
            Builders<GraphInstance>.Filter.Eq(g => g.UserId, userId)
        );
        return await DocumentExistsByPredicateAsync(filter);
    }

    public Task<bool> Exists(string graphId, string name, string userId)
    {
        FilterDefinition<GraphInstance> filter = Builders<GraphInstance>.Filter.And(
            Builders<GraphInstance>.Filter.Eq(g => g.GraphId, graphId),
            Builders<GraphInstance>.Filter.Eq(g => g.Name, name),
            Builders<GraphInstance>.Filter.Eq(g => g.UserId, userId)
        );
        return DocumentExistsByPredicateAsync(filter);
    }
}
