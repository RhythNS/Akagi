using Akagi.Web.Models.TimeTrackers;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Web.Data;

public interface IEntryDatabase : IDatabase<Entry>
{
    public Task<object[]> GetDistinctValuesAsync(string name);
}

public class EntryDatabase : Database<Entry>, IEntryDatabase
{
    public EntryDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "entries")
    {
    }

    public async Task<object[]> GetDistinctValuesAsync(string name)
    {
        IMongoCollection<Entry> collection = GetCollection();
        FieldDefinition<Entry, object> field = new StringFieldDefinition<Entry, object>($"Values.{name}");
        IAsyncCursor<object> distinctValues = await collection.DistinctAsync(field, FilterDefinition<Entry>.Empty);
        List<object> values = await distinctValues.ToListAsync();
        return [.. values];
    }
}
