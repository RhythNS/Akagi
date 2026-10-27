using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Characters.Routines;

internal interface IRoutineDatabase : IDatabase<Routine>
{
    public Task<List<Routine>> GetRoutines(IEnumerable<string> ids);

    /// <summary>
    /// Routines a character scheduled for itself.
    /// </summary>
    public Task<List<Routine>> GetOwnedRoutines(string characterId);

    /// <summary>
    /// Characters that own at least one enabled self-made routine.
    /// </summary>
    public Task<List<string>> GetOwnerCharacterIds();
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

    public async Task<List<Routine>> GetOwnedRoutines(string characterId)
    {
        FilterDefinition<Routine> filter = Builders<Routine>.Filter.Eq(r => r.OwnerCharacterId, characterId);
        List<Routine> routines = await GetDocumentsByPredicateAsync(filter);
        await Task.WhenAll(routines.Select(r => r.AfterLoad()));
        return routines;
    }

    public async Task<List<string>> GetOwnerCharacterIds()
    {
        // $gt "" skips both empty and missing fields; $ne would match graph routines stored before the field existed
        FilterDefinition<Routine> filter = Builders<Routine>.Filter.Gt(r => r.OwnerCharacterId, string.Empty)
            & Builders<Routine>.Filter.Eq(r => r.Enabled, true);
        List<Routine> routines = await GetDocumentsByPredicateAsync(filter);
        return [.. routines.Select(r => r.OwnerCharacterId).Distinct()];
    }
}
