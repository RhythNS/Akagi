namespace Akagi.Data;

internal interface IDatabaseFactory
{
    public Task<bool> TrySave(Savable savable);
    public IDatabase GetDatabase(Savable savable);
}
