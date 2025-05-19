using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Data;

internal class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}

internal abstract class Database<T> : IDatabase<T> where T : ISavable
{
    private string _connectionString;
    private string _databaseName;
    private string _collectionName;
    private IMongoDatabase _mongoDatabase;

    public Database(IOptionsMonitor<DatabaseOptions> options, string collectionName)
    {
        options.OnChange(OnOptionsChange);
        OnOptionsChange(options.CurrentValue, collectionName);
    }

    private void OnOptionsChange(DatabaseOptions options, string collectionName)
    {
        _connectionString = options.ConnectionString;
        _databaseName = options.DatabaseName;
        _collectionName = collectionName;

        InitializeMongoDatabase();
    }

    private void InitializeMongoDatabase()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_databaseName) || string.IsNullOrWhiteSpace(_collectionName))
        {
            throw new InvalidOperationException("ConnectionString, DatabaseName, and CollectionName must be provided.");
        }

        MongoClient client = new(_connectionString);
        _mongoDatabase = client.GetDatabase(_databaseName);
    }

    private IMongoCollection<T> GetCollection()
    {
        if (_mongoDatabase == null)
        {
            throw new InvalidOperationException("MongoDB database is not initialized.");
        }

        return _mongoDatabase.GetCollection<T>(_collectionName);
    }

    public async Task<List<T>> GetDocumentsAsync()
    {
        IMongoCollection<T> collection = GetCollection();
        return await collection.Find(_ => true).ToListAsync();
    }

    public async Task<T?> GetDocumentByIdAsync(string id)
    {
        IMongoCollection<T> collection = GetCollection();
        FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, id);
        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task SaveDocumentAsync(T document)
    {
        IMongoCollection<T> collection = GetCollection();

        if (string.IsNullOrEmpty(document.Id))
        {
            await collection.InsertOneAsync(document);
        }
        else
        {
            FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, document.Id);
            ReplaceOptions options = new() { IsUpsert = true };
            await collection.ReplaceOneAsync(filter, document, options);
        }
    }

    public async Task DeleteDocumentByIdAsync(string id)
    {
        IMongoCollection<T> collection = GetCollection();
        FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, id);
        await collection.DeleteOneAsync(filter);
    }

    public async Task BulkInsertAsync(IEnumerable<T> documents)
    {
        IMongoCollection<T> collection = GetCollection();
        await collection.InsertManyAsync(documents);
    }

    public async Task UpdateDocumentAsync(string id, UpdateDefinition<T> updateDefinition)
    {
        IMongoCollection<T> collection = GetCollection();
        FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, id);
        await collection.UpdateOneAsync(filter, updateDefinition);
    }

    public async Task<List<T>> GetDocumentsByPredicateAsync(FilterDefinition<T> predicate)
    {
        IMongoCollection<T> collection = GetCollection();
        return await collection.Find(predicate).ToListAsync();
    }
}
