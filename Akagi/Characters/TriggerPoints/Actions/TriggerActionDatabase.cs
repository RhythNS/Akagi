using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.TriggerPoints.Actions;

internal interface ITriggerActionDatabase : IDatabase<TriggerAction>;

internal class TriggerActionDatabase : Database<TriggerAction>, ITriggerActionDatabase
{
    public override string CollectionName => "trigger_actions";

    public TriggerActionDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable)
    {
        return savable is TriggerAction;
    }

    public override Task SaveAsync(Savable savable)
    {
        return SaveDocumentAsync((TriggerAction)savable);
    }
}
