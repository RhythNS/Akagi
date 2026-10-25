using Akagi.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Memories;

internal class MemoryDatabase : Database<MemoryRecord>, IMemoryDatabase
{
    public override string CollectionName => "memories";

    private readonly ILogger<MemoryDatabase> _logger;
    private bool _indexesCreated;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public MemoryDatabase(
        IOptionsMonitor<DatabaseOptions> options,
        ILogger<MemoryDatabase> logger) : base(options)
    {
        _logger = logger;
    }

    public override bool CanSave(Savable savable) => savable is MemoryRecord;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((MemoryRecord)savable);

    public async Task<List<MemoryRecord>> GetActiveMemoriesAsync(string characterId, string? userId, bool includeGlobalLore = true)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return [];
        }

        await EnsureIndexesOnceAsync();

        FilterDefinitionBuilder<MemoryRecord> builder = Builders<MemoryRecord>.Filter;
        FilterDefinition<MemoryRecord> filter = builder.Eq(m => m.CharacterId, characterId) &
                                               builder.Eq(m => m.IsArchived, false);

        if (string.IsNullOrEmpty(userId))
        {
            filter &= builder.Eq(m => m.UserId, null);
        }
        else if (includeGlobalLore)
        {
            filter &= builder.Or(
                builder.Eq(m => m.UserId, userId),
                builder.Eq(m => m.UserId, null)
            );
        }
        else
        {
            filter &= builder.Eq(m => m.UserId, userId);
        }

        return await GetDocumentsByPredicateAsync(filter);
    }

    public async Task<List<MemoryRecord>> GetAllMemoriesAsync(string characterId, string? userId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return [];
        }

        await EnsureIndexesOnceAsync();

        FilterDefinitionBuilder<MemoryRecord> builder = Builders<MemoryRecord>.Filter;
        FilterDefinition<MemoryRecord> filter = builder.Eq(m => m.CharacterId, characterId);

        if (string.IsNullOrEmpty(userId))
        {
            filter &= builder.Eq(m => m.UserId, null);
        }
        else
        {
            filter &= builder.Or(
                builder.Eq(m => m.UserId, userId),
                builder.Eq(m => m.UserId, null)
            );
        }

        return await GetDocumentsByPredicateAsync(filter);
    }

    public async Task ArchiveMemoryAsync(string memoryId)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return;
        }

        UpdateDefinition<MemoryRecord> update = Builders<MemoryRecord>.Update.Set(m => m.IsArchived, true);
        await UpdateDocumentAsync(memoryId, update);
    }

    public async Task UpdateLastAccessedAsync(string memoryId, DateTime accessedAt)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return;
        }

        UpdateDefinition<MemoryRecord> update = Builders<MemoryRecord>.Update.Set(m => m.LastAccessed, accessedAt);
        await UpdateDocumentAsync(memoryId, update);
    }

    public async Task UpdateLastAccessedBatchAsync(IEnumerable<string> memoryIds, DateTime accessedAt)
    {
        string[] idArray = memoryIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (idArray.Length == 0)
        {
            return;
        }

        FilterDefinition<MemoryRecord> filter = Builders<MemoryRecord>.Filter.In(m => m.Id, idArray);
        UpdateDefinition<MemoryRecord> update = Builders<MemoryRecord>.Update.Set(m => m.LastAccessed, accessedAt);

        IMongoCollection<MemoryRecord> collection = GetCollection();
        await collection.UpdateManyAsync(filter, update);
    }

    public async Task EnsureIndexesAsync()
    {
        try
        {
            IMongoCollection<MemoryRecord> collection = GetCollection();

            CreateIndexModel<MemoryRecord> compoundIndex = new(
                Builders<MemoryRecord>.IndexKeys
                    .Ascending(m => m.CharacterId)
                    .Ascending(m => m.UserId)
                    .Ascending(m => m.IsArchived),
                new CreateIndexOptions { Background = true, Name = "IX_Memories_Character_User_Archived" });

            CreateIndexModel<MemoryRecord> characterIndex = new(
                Builders<MemoryRecord>.IndexKeys
                    .Ascending(m => m.CharacterId)
                    .Ascending(m => m.IsArchived),
                new CreateIndexOptions { Background = true, Name = "IX_Memories_Character_Archived" });

            await collection.Indexes.CreateManyAsync([compoundIndex, characterIndex]);
            _logger.LogDebug("Indexes ensured for MongoDB collection '{CollectionName}'.", CollectionName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create compound indexes on collection '{CollectionName}'.", CollectionName);
        }
    }

    private async Task EnsureIndexesOnceAsync()
    {
        if (_indexesCreated)
        {
            return;
        }

        await _indexLock.WaitAsync();
        try
        {
            if (!_indexesCreated)
            {
                await EnsureIndexesAsync();
                _indexesCreated = true;
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }
}
