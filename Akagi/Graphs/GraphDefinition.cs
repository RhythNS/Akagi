using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Akagi.Graphs;

/// <summary>
/// The raw JSON of an uploaded graph. Stored so that other graphs can reference it by GraphId or file name.
/// </summary>
internal class GraphDefinition : Savable
{
    private string _graphId = string.Empty;
    private string _userId = string.Empty;
    private string _fileName = string.Empty;
    private string _json = string.Empty;
    private DateTime _uploadedAt = DateTime.UtcNow;

    public string GraphId
    {
        get => _graphId;
        set => SetProperty(ref _graphId, value);
    }
    [BsonRepresentation(MongoDB.Bson.BsonType.ObjectId)]
    public string UserId
    {
        get => _userId;
        set => SetProperty(ref _userId, value);
    }
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }
    public string Json
    {
        get => _json;
        set => SetProperty(ref _json, value);
    }
    public DateTime UploadedAt
    {
        get => _uploadedAt;
        set => SetProperty(ref _uploadedAt, value);
    }
}

internal interface IGraphDefinitionDatabase : IDatabase<GraphDefinition>
{
    public Task<GraphDefinition?> GetByGraphId(string graphId, string userId);
    public Task<GraphDefinition?> GetByFileName(string fileName, string userId);
    public Task Store(string graphId, string fileName, string json, string userId);
}

internal class GraphDefinitionDatabase : Database<GraphDefinition>, IGraphDefinitionDatabase
{
    public override string CollectionName => "graph_definitions";

    public GraphDefinitionDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is GraphDefinition;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((GraphDefinition)savable);

    public async Task<GraphDefinition?> GetByGraphId(string graphId, string userId)
    {
        FilterDefinition<GraphDefinition> filter = Builders<GraphDefinition>.Filter.And(
            Builders<GraphDefinition>.Filter.Eq(g => g.GraphId, graphId),
            Builders<GraphDefinition>.Filter.Eq(g => g.UserId, userId));
        List<GraphDefinition> results = await GetDocumentsByPredicateAsync(filter);
        return results.OrderByDescending(g => g.UploadedAt).FirstOrDefault();
    }

    public async Task<GraphDefinition?> GetByFileName(string fileName, string userId)
    {
        FilterDefinition<GraphDefinition> filter = Builders<GraphDefinition>.Filter.And(
            Builders<GraphDefinition>.Filter.Eq(g => g.FileName, fileName),
            Builders<GraphDefinition>.Filter.Eq(g => g.UserId, userId));
        List<GraphDefinition> results = await GetDocumentsByPredicateAsync(filter);
        return results.OrderByDescending(g => g.UploadedAt).FirstOrDefault();
    }

    public async Task Store(string graphId, string fileName, string json, string userId)
    {
        GraphDefinition definition = await GetByGraphId(graphId, userId) ?? new GraphDefinition
        {
            GraphId = graphId,
            UserId = userId
        };
        definition.FileName = fileName;
        definition.Json = json;
        definition.UploadedAt = DateTime.UtcNow;
        await SaveDocumentAsync(definition);
    }
}
