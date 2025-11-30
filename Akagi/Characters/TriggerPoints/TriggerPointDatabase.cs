using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.TriggerPoints;

internal interface ITriggerPointDatabase : IDatabase<TriggerPoint>;

internal class TriggerPointDatabase : Database<TriggerPoint>, ITriggerPointDatabase
{
    public TriggerPointDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "triggerPoint")
    {
    }

    public override bool CanSave(Savable savable)
    {
        return savable is TriggerPoint;
    }

    public override Task SaveAsync(Savable savable)
    {
        return SaveDocumentAsync((TriggerPoint)savable);
    }
}
