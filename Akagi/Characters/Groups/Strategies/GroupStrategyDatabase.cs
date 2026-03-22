using Akagi.Data;
using Microsoft.Extensions.Options;

namespace Akagi.Characters.Groups.Strategies;

internal interface IGroupStrategyDatabase : IDatabase<GroupStrategy>
{
}

internal class GroupStrategyDatabase : Database<GroupStrategy>, IGroupStrategyDatabase
{
    public override string CollectionName => "groupStrategies";

    public GroupStrategyDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options)
    {
    }

    public override bool CanSave(Savable savable) => savable is GroupStrategy;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((GroupStrategy)savable);
}
