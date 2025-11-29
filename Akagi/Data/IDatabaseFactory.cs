namespace Akagi.Data;

internal interface IDatabaseFactory
{
    public Task<bool> TrySave(Savable savable);
    public Task<bool> SaveIfDirty(Savable savable);
    public IDatabase GetDatabase(Savable savable);
    public T GetDatabase<T>() where T : IDatabase;
}
