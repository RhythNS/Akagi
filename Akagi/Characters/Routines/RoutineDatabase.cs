using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Routines;

internal interface IRoutineDatabase : IDatabase<Routine>
{
    public Task<List<Routine>> GetRoutines(IEnumerable<string> ids);
}

internal class RoutineDatabase : Database<Routine>, IRoutineDatabase
{
    public override string CollectionName => "routines";

    public RoutineDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Routine;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Routine)savable);

    public async Task<List<Routine>> GetRoutines(IEnumerable<string> ids)
    {
        FilterDefinition<Routine> filter = Builders<Routine>.Filter.In(r => r.Id, ids);
        List<Routine> routines = await GetDocumentsByPredicateAsync(filter);
        await Task.WhenAll(routines.Select(r => r.AfterLoad()));
        return routines;
    }
}
