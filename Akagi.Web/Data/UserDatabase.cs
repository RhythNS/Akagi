using Akagi.Web.Models;
using Microsoft.Extensions.Options;

namespace Akagi.Web.Data;

public interface IUserDatabase : IDatabase<User>;

public class UserDatabase : Database<User>, IUserDatabase
{
    public UserDatabase(IOptionsMonitor<DatabaseOptions> options) : base(options, "users")
    {
    }
}
