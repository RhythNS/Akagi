using Akagi.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Users;

internal class UserDatabase : Database<User>, IUserDatabase
{
    private ILogger<UserDatabase> _logger;

    public UserDatabase(IOptionsMonitor<DatabaseOptions> options, ILogger<UserDatabase> logger) : base(options, "User")
    {
        _logger = logger;
    }

    public async Task<User?> GetUser(FilterDefinition<User> user)
    {
        List<User> users = await GetDocumentsByPredicateAsync(user);
        if (users.Count == 0)
        {
            _logger.LogWarning("User not found with the provided filter: {Filter}", user);
            return null;
        }
        else if (users.Count > 1)
        {
            _logger.LogWarning("Multiple users found with the provided filter: {Filter}", user);
            return null;
        }
        else
        {
            return users[0];
        }
    }
}
