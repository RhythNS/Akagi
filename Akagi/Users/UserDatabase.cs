using Akagi.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Akagi.Users;

internal class UserDatabase : Database<User>, IUserDatabase
{
    private readonly ILogger<UserDatabase> _logger;

    public UserDatabase(IOptionsMonitor<DatabaseOptions> options,
                        ILogger<UserDatabase> logger) : base(options, "user")
    {
        _logger = logger;
    }

    public override bool CanSave(Savable savable) => savable is User;

    public override Task SaveAsync(Savable savable) => SaveDocumentAsync((User)savable);

    public Task<User?> GetByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogInformation("Attempted to get user by an empty or null username.");
            return Task.FromResult<User?>(null);
        }
        FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.Username, username);
        return GetUser(filter);
    }

    public async Task<User?> GetUser(FilterDefinition<User> user)
    {
        List<User> users = await GetDocumentsByPredicateAsync(user);
        if (users.Count == 0)
        {
            _logger.LogInformation("User not found with the provided filter: {Filter}", user);
            return null;
        }
        else if (users.Count > 1)
        {
            _logger.LogInformation("Multiple users found with the provided filter: {Filter}", user);
            return null;
        }
        else
        {
            await users[0].AfterLoad();
            return users[0];
        }
    }
}
