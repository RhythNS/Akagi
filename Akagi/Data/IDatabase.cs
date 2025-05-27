using MongoDB.Driver;

namespace Akagi.Data;

internal interface IDatabase<T> where T : Savable
{
    public Task<List<T>> GetDocumentsAsync();
    public Task<T?> GetDocumentByIdAsync(string id);
    public Task SaveDocumentAsync(T document);
    public Task<bool> SaveFromFile(MemoryStream stream);
    public Task<bool> SaveFromBSON(MemoryStream stream);
    public Task DeleteDocumentByIdAsync(string id);
    public Task BulkInsertAsync(IEnumerable<T> documents);
    public Task UpdateDocumentAsync(string id, UpdateDefinition<T> updateDefinition);
    public Task<List<T>> GetDocumentsByPredicateAsync(FilterDefinition<T> predicate);
    public Task<bool> DocumentExistsAsync(string id);
    public Task<bool> DocumentExistsByPredicateAsync(FilterDefinition<T> predicate);
}
