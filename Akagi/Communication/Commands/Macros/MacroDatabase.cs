using Akagi.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Communication.Commands.Macros;

internal interface IMacroDatabase : IDatabase<Macro>
{
    Task<List<Macro>> GetMacrosForUserAsync(string userId);
    Task<Macro?> GetMacroByNameAsync(string userId, string macroName);
}

internal class MacroDatabase : Database<Macro>, IMacroDatabase
{
    public override string CollectionName => "macros";

    public MacroDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is Macro;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((Macro)savable);

    public async Task<List<Macro>> GetMacrosForUserAsync(string userId)
    {
        FilterDefinition<Macro> filter = Builders<Macro>.Filter.Eq(m => m.UserId, userId);
        return await GetDocumentsByPredicateAsync(filter);
    }

    public async Task<Macro?> GetMacroByNameAsync(string userId, string macroName)
    {
        FilterDefinition<Macro> filter = Builders<Macro>.Filter.And(
            Builders<Macro>.Filter.Eq(m => m.UserId, userId),
            Builders<Macro>.Filter.Eq(m => m.Name, macroName)
        );
        List<Macro> macros = await GetDocumentsByPredicateAsync(filter);
        return macros.FirstOrDefault();
    }
}
