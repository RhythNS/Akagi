using Akagi.Data;

namespace Akagi.Characters.Memories;

internal interface IMemoryDatabase : IDatabase<MemoryRecord>
{
    public Task<List<MemoryRecord>> GetActiveMemoriesAsync(string characterId, string? userId, bool includeGlobalLore = true);

    public Task<List<MemoryRecord>> GetAllMemoriesAsync(string characterId, string? userId);

    public Task ArchiveMemoryAsync(string memoryId);

    public Task UpdateLastAccessedAsync(string memoryId, DateTime accessedAt);

    public Task UpdateLastAccessedBatchAsync(IEnumerable<string> memoryIds, DateTime accessedAt);

    public Task EnsureIndexesAsync();
}
