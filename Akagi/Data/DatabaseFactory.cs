namespace Akagi.Data;

internal interface IDatabaseFactory
{
    public Task TrySave(Savable savable);
    public Task SaveIfDirty(Savable savable);
    public IDatabase GetDatabase(Savable savable);
    public IDatabase GetDatabase(string collectionName);
    public T GetDatabase<T>() where T : IDatabase;
    public IDatabase<T> GetDatabase<T>(Type savableType) where T : Savable;
}

internal class DatabaseFactory : IDatabaseFactory
{
    private readonly IDatabase[] _databases;

    public DatabaseFactory(IEnumerable<IDatabase> databases)
    {
        _databases = [.. databases];
    }

    public IDatabase GetDatabase(Savable savable)
    {
        IDatabase? database = _databases.FirstOrDefault(db => db.CanSave(savable));
        if (database == null)
        {
            throw new InvalidOperationException($"No database found that can save {savable.GetType().Name}.");
        }
        return database;
    }

    public T GetDatabase<T>() where T : IDatabase
    {
        IDatabase? database = _databases.OfType<T>().FirstOrDefault();
        if (database == null)
        {
            throw new InvalidOperationException($"No database of type {typeof(T).Name} found.");
        }
        return (T)database;
    }

    public IDatabase<T> GetDatabase<T>(Type savableType) where T : Savable
    {
        IDatabase? database = _databases.FirstOrDefault(db =>
        {
            Type dbType = db.GetType();
            Type[] interfaces = dbType.GetInterfaces();
            foreach (Type iface in interfaces)
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDatabase<>))
                {
                    Type genericArg = iface.GetGenericArguments()[0];
                    if (genericArg == savableType)
                    {
                        return true;
                    }
                }
            }
            return false;
        });
        if (database == null)
        {
            throw new InvalidOperationException($"No database found that can save type {savableType.Name}.");
        }
        return (IDatabase<T>)database;
    }

    public IDatabase GetDatabase(string collectionName)
    {
        return _databases.FirstOrDefault(db => string.Equals(collectionName, db.CollectionName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No database found with collection name '{collectionName}'.");
    }

    public async Task SaveIfDirty(Savable savable)
    {
        if (savable.Dirty)
        {
            await TrySave(savable);
        }
    }

    public async Task TrySave(Savable savable)
    {
        Task[] trackingTasks = [.. savable.ToTrack
            .Where(s => s.Dirty)
            .Select(SaveIfDirty)];

        await Task.WhenAll(trackingTasks);

        IDatabase? database = _databases.FirstOrDefault(db => db.CanSave(savable));
        if (database == null)
        {
            return;
        }
        await database.SaveAsync(savable);
    }
}
