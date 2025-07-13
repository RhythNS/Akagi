using Akagi.Web.Models.TimeTrackers;
using Microsoft.Extensions.Options;

namespace Akagi.Web.Data;

public interface IEntryDatabase : IDatabase<Entry>;

public class EntryDatabase : Database<Entry>, IEntryDatabase
{
    public EntryDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "entries")
    {
    }
}
