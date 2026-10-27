using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Life;

internal interface ILifeProfileDatabase : IDatabase<LifeProfile>
{
    public Task<LifeProfile?> GetForUser(string userId);
    public Task<LifeProfile> GetOrCreate(string userId);
}

internal class LifeProfileDatabase : Database<LifeProfile>, ILifeProfileDatabase
{
    public override string CollectionName => "life_profiles";

    public LifeProfileDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is LifeProfile;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((LifeProfile)savable);

    public async Task<LifeProfile?> GetForUser(string userId)
    {
        FilterDefinition<LifeProfile> filter = Builders<LifeProfile>.Filter.Eq(p => p.UserId, userId);
        LifeProfile? profile = (await GetDocumentsByPredicateAsync(filter)).FirstOrDefault();
        if (profile != null)
        {
            await profile.AfterLoad();
        }
        return profile;
    }

    public async Task<LifeProfile> GetOrCreate(string userId)
    {
        LifeProfile? profile = await GetForUser(userId);
        if (profile != null)
        {
            return profile;
        }

        profile = new LifeProfile { UserId = userId };
        await SaveDocumentAsync(profile);
        return profile;
    }
}
