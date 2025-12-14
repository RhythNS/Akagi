namespace Akagi.Data;

internal interface IDatabaseFactory
{
    public Task TrySave(Savable savable);
    public Task SaveIfDirty(Savable savable);
    public IDatabase GetDatabase(Savable savable);
    public T GetDatabase<T>() where T : IDatabase;
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
