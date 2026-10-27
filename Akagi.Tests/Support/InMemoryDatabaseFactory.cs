using Akagi.Bridge.Attributes;
using Akagi.Data;
using Akagi.Graphs;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Reflection;

namespace Akagi.Tests.Support;

/// <summary>
/// Stores savables in memory. Collections are named after the root graph node type of each savable.
/// </summary>
internal class InMemoryDatabaseFactory : IDatabaseFactory
{
    private readonly Dictionary<string, Savable> _documents = [];
    private readonly InMemoryGraphInstanceDatabase _graphInstances = new();
    private readonly List<IDatabase> _extraDatabases = [];

    public IReadOnlyDictionary<string, Savable> Documents => _documents;
    public InMemoryGraphInstanceDatabase GraphInstances => _graphInstances;

    public void Register(IDatabase database)
    {
        _extraDatabases.Add(database);
    }

    public IEnumerable<T> All<T>() where T : Savable
    {
        return _documents.Values.OfType<T>();
    }

    public IDatabase GetDatabase(Savable savable)
    {
        if (savable is GraphInstance)
        {
            return _graphInstances;
        }
        IDatabase? extra = _extraDatabases.FirstOrDefault(d => d.CanSave(savable));
        return extra ?? new Collection(this, CollectionName(savable.GetType()));
    }

    public IDatabase GetDatabase(string collectionName)
    {
        return new Collection(this, collectionName);
    }

    public T GetDatabase<T>() where T : IDatabase
    {
        if (_graphInstances is T graphDatabase)
        {
            return graphDatabase;
        }
        IDatabase? extra = _extraDatabases.FirstOrDefault(d => d is T);
        return extra != null ? (T)extra : throw new InvalidOperationException($"No in-memory database for {typeof(T).Name}");
    }

    public IDatabase<T> GetDatabase<T>(Type savableType) where T : Savable
    {
        throw new NotSupportedException();
    }

    public async Task SaveIfDirty(Savable savable)
    {
        if (savable.Dirty)
        {
            await TrySave(savable);
        }
    }

    public Task TrySave(Savable savable)
    {
        if (savable is GraphInstance graphInstance)
        {
            return _graphInstances.SaveAsync(graphInstance);
        }
        IDatabase? extra = _extraDatabases.FirstOrDefault(d => d.CanSave(savable));
        if (extra != null)
        {
            return extra.SaveAsync(savable);
        }

        if (string.IsNullOrEmpty(savable.Id))
        {
            savable.Id = ObjectId.GenerateNewId().ToString();
        }
        _documents[savable.Id] = savable;
        savable.Dirty = false;
        return Task.CompletedTask;
    }

    private static string CollectionName(Type type)
    {
        Type current = type;
        while (current.BaseType != null && current.BaseType.GetCustomAttribute<GraphNodeAttribute>() != null)
        {
            current = current.BaseType;
        }
        return current.Name;
    }

    private class Collection : IDatabase
    {
        private readonly InMemoryDatabaseFactory _owner;

        public Collection(InMemoryDatabaseFactory owner, string collectionName)
        {
            _owner = owner;
            CollectionName = collectionName;
        }

        public string CollectionName { get; }

        public bool CanSave(Savable savable) => CollectionName == InMemoryDatabaseFactory.CollectionName(savable.GetType());

        public Task SaveAsync(Savable savable) => _owner.TrySave(savable);

        // Called through reflection by GraphLoader
        public Task<Savable?> GetDocumentByIdAsync(string id)
        {
            _owner._documents.TryGetValue(id, out Savable? savable);
            return Task.FromResult(savable);
        }
    }
}

internal class InMemoryGraphInstanceDatabase : InMemoryDatabase<GraphInstance>, IGraphInstanceDatabase
{
    public InMemoryGraphInstanceDatabase() : base("graph_instances")
    {
    }

    public Task<GraphInstance[]> GetGraphs(string userId) =>
        Task.FromResult(Items.Where(g => g.UserId == userId).ToArray());

    public Task<GraphInstance[]> GetGraphs(string graphId, string userId) =>
        Task.FromResult(Items.Where(g => g.GraphId == graphId && g.UserId == userId).ToArray());

    public Task<GraphInstance?> GetGraph(string graphId, string name, string userId) =>
        Task.FromResult(Items.FirstOrDefault(g => g.GraphId == graphId && g.Name == name && g.UserId == userId));

    public Task<bool> Exists(string graphId, string userId) =>
        Task.FromResult(Items.Any(g => g.GraphId == graphId && g.UserId == userId));

    public Task<bool> Exists(string graphId, string name, string userId) =>
        Task.FromResult(Items.Any(g => g.GraphId == graphId && g.Name == name && g.UserId == userId));
}

/// <summary>
/// Generic in-memory implementation of the typed database interface. Mongo specific queries are not supported.
/// </summary>
internal class InMemoryDatabase<T> : IDatabase<T> where T : Savable
{
    private readonly Dictionary<string, T> _items = [];

    public InMemoryDatabase(string collectionName)
    {
        CollectionName = collectionName;
    }

    public string CollectionName { get; }

    public IEnumerable<T> Items => _items.Values;

    public bool CanSave(Savable savable) => savable is T;

    public Task SaveAsync(Savable savable) => SaveDocumentAsync((T)savable);

    public Task<List<T>> GetDocumentsAsync() => Task.FromResult(_items.Values.ToList());

    public Task<T?> GetDocumentByIdAsync(string id)
    {
        _items.TryGetValue(id, out T? item);
        return Task.FromResult(item);
    }

    public Task SaveDocumentAsync(T document)
    {
        if (string.IsNullOrEmpty(document.Id))
        {
            document.Id = ObjectId.GenerateNewId().ToString();
        }
        _items[document.Id] = document;
        document.Dirty = false;
        return Task.CompletedTask;
    }

    public Task DeleteDocumentByIdAsync(string id)
    {
        _items.Remove(id);
        return Task.CompletedTask;
    }

    public Task<bool> DocumentExistsAsync(string id) => Task.FromResult(_items.ContainsKey(id));

    public async Task BulkInsertAsync(IEnumerable<T> documents)
    {
        foreach (T document in documents)
        {
            await SaveDocumentAsync(document);
        }
    }

    public Task<bool> SaveFromFile(MemoryStream stream) => throw new NotSupportedException();
    public Task<bool> SaveFromBSON(MemoryStream stream) => throw new NotSupportedException();
    public Task UpdateDocumentAsync(string id, UpdateDefinition<T> updateDefinition) => throw new NotSupportedException();
    public Task<List<T>> GetDocumentsByPredicateAsync(FilterDefinition<T> predicate) => throw new NotSupportedException();
    public Task<bool> DocumentExistsByPredicateAsync(FilterDefinition<T> predicate) => throw new NotSupportedException();
}
