using MongoDB.Driver;

namespace Akagi.Data;

internal interface IDatabase<T> where T : ISavable
{
    public Task<List<T>> GetDocumentsAsync();
    public Task<T?> GetDocumentByIdAsync(string id);
    public Task SaveDocumentAsync(T document);
    public Task DeleteDocumentByIdAsync(string id);
    public Task BulkInsertAsync(IEnumerable<T> documents);
    public Task UpdateDocumentAsync(string id, UpdateDefinition<T> updateDefinition);
    public Task<List<T>> GetDocumentsByPredicateAsync(FilterDefinition<T> predicate);
}
