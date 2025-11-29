using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace Akagi.Data;

internal class FileDatabase : IFileDatabase
{
    protected IMongoDatabase _database = default!;
    protected GridFSBucket _gridFS = default!;

    private string _connectionString = string.Empty;
    private string _databaseName = string.Empty;
    private readonly string _collectionName = "files";

    public FileDatabase(IOptionsMonitor<DatabaseOptions> options)
    {
        options.OnChange(OnOptionsChange);
        OnOptionsChange(options.CurrentValue);
    }

    private void OnOptionsChange(DatabaseOptions options)
    {
        _connectionString = options.ConnectionString;
        _databaseName = options.DatabaseName;

        InitializeMongoDatabase();
    }

    private void InitializeMongoDatabase()
    {
        if (string.IsNullOrWhiteSpace(_connectionString) || string.IsNullOrWhiteSpace(_databaseName) || string.IsNullOrWhiteSpace(_collectionName))
        {
            throw new InvalidOperationException("ConnectionString, DatabaseName, and CollectionName must be provided.");
        }

        MongoClient client = new(_connectionString);
        _database = client.GetDatabase(_databaseName);
        _gridFS = new GridFSBucket(_database);
    }

    public async Task<ObjectId> UploadFileAsync(Stream fileStream, string fileName, string contentType)
    {
        return await _gridFS.UploadFromStreamAsync(fileName, fileStream);
    }

    public async Task<Stream> DownloadFileAsync(ObjectId id)
    {
        MemoryStream stream = new();
        await _gridFS.DownloadToStreamAsync(id, stream);
        stream.Position = 0;
        return stream;
    }

    public Task DeleteFileAsync(ObjectId id)
    {
        return _gridFS.DeleteAsync(id);
    }
}
