using Akagi.Web.Models.TimeTrackers;
using Microsoft.Extensions.Options;

namespace Akagi.Web.Data;

public interface IDefinitionDatabase : IDatabase<Definition>;

public class DefinitionDatabase : Database<Definition>, IDefinitionDatabase
{
    public DefinitionDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "definitions")
    {
    }
}
