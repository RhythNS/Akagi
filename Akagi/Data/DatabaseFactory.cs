namespace Akagi.Data;

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

    public Task<bool> SaveIsDirty(Savable savable)
    {
        if (savable.Dirty)
        {
            return TrySave(savable);
        }

        return Task.FromResult(false);
    }

    public async Task<bool> TrySave(Savable savable)
    {
        IDatabase? database = _databases.FirstOrDefault(db => db.CanSave(savable));
        if (database == null)
        {
            return false;
        }
        await database.SaveAsync(savable);
        return true;
    }
}
