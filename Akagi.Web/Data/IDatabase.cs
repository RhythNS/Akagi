using MongoDB.Driver;

namespace Akagi.Web.Data;

public interface IDatabase<T> where T : Savable
{
    public Task<T> SaveDocumentAsync(T document);
    public Task<List<T>> GetDocumentsAsync();
    public Task<T?> GetDocumentByIdAsync(string id);
    public Task<List<T>> GetDocumentsByPredicateAsync(FilterDefinition<T> predicate);
    public Task DeleteDocumentAsync(string id);
}
