using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;

namespace Akagi.Data;

internal class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}

internal abstract class Database<T> : IDatabase<T> where T : Savable
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

    public async Task<bool> SaveFromFile(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(stream);
        string json = reader.ReadToEnd();

        T? t = default;
        try
        {
            t = JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception)
        {
            return false;
        }
        if (t == null)
        {
            return false;
        }
        try
        {
            await SaveDocumentAsync(t);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    public async Task<bool> SaveFromBSON(MemoryStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(stream);
        string json = reader.ReadToEnd();

        try
        {
            BsonDocument bsonDoc = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(json);
            IMongoCollection<T> collection = GetCollection();
            IMongoCollection<BsonDocument> bsonCollection = _mongoDatabase.GetCollection<BsonDocument>(_collectionName);

            if (bsonDoc.Contains("Id") && bsonDoc["Id"].IsString)
            {
                FilterDefinition<BsonDocument> filter = Builders<BsonDocument>.Filter.Eq("Id", bsonDoc["Id"].AsString);
                ReplaceOptions options = new() { IsUpsert = true };
                await bsonCollection.ReplaceOneAsync(filter, bsonDoc, options);
            }
            else
            {
                await bsonCollection.InsertOneAsync(bsonDoc);
            }
        }
        catch (Exception)
        {
            return false;
        }
        return true;
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

    public async Task<bool> DocumentExistsAsync(string id)
    {
        IMongoCollection<T> collection = GetCollection();
        FilterDefinition<T> filter = Builders<T>.Filter.Eq(y => y.Id, id);
        long count = await collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 });
        return count > 0;
    }

    public async Task<bool> DocumentExistsByPredicateAsync(FilterDefinition<T> predicate)
    {
        IMongoCollection<T> collection = GetCollection();
        long count = await collection.CountDocumentsAsync(predicate, new CountOptions { Limit = 1 });
        return count > 0;
    }
}
