using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Web.Data;

public class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}

public abstract class Database<T> : IDatabase<T> where T : Savable
{
    private string _connectionString = string.Empty;
    private string _databaseName = string.Empty;
    private string _collectionName = string.Empty;
    private IMongoDatabase _mongoDatabase = default!;

    public Database(IOptionsMonitor<DatabaseOptions> options, string collectionName)
    {
        options.OnChange((options, _) => OnOptionsChange(options, collectionName));
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

    protected IMongoCollection<T> GetCollection()
    {
        if (_mongoDatabase == null)
        {
            throw new InvalidOperationException("MongoDB database is not initialized.");
        }

        return _mongoDatabase.GetCollection<T>(_collectionName);
    }

    public async Task<T> SaveDocumentAsync(T document)
    {
        IMongoCollection<T> collection = GetCollection();
        if (string.IsNullOrEmpty(document.Id))
        {
            await collection.InsertOneAsync(document);
            return document;
        }
        else
        {
            FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, document.Id);
            ReplaceOptions options = new() { IsUpsert = true };
            ReplaceOneResult result = await collection.ReplaceOneAsync(filter, document, options);
            return document;
        }
    }

    public Task<List<T>> GetDocumentsAsync()
    {
        IMongoCollection<T> collection = GetCollection();
        return collection.Find(Builders<T>.Filter.Empty).ToListAsync();
    }

    public async Task<T?> GetDocumentByIdAsync(string id)
    {
        IMongoCollection<T> collection = GetCollection();
        FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, id);
        T document = await collection.Find(filter).FirstOrDefaultAsync();
        return document;
    }

    public Task<List<T>> GetDocumentsByPredicateAsync(FilterDefinition<T> predicate)
    {
        IMongoCollection<T> collection = GetCollection();
        return collection.Find(predicate).ToListAsync();
    }

    public Task DeleteDocumentAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Id cannot be null or empty.", nameof(id));
        }
        IMongoCollection<T> collection = GetCollection();
        return collection.DeleteOneAsync(Builders<T>.Filter.Eq(doc => doc.Id, id));
    }
}
