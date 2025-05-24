using Akagi.Data;
using MongoDB.Driver;

namespace Akagi.Users;

internal interface IUserDatabase : IDatabase<User>
{
    public Task<User?> GetUser(FilterDefinition<User> user);

    public Task<User?> GetByUsername(string username);
}
