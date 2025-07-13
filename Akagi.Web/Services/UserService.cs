using Akagi.Web.Data;
using Akagi.Web.Models;
using MongoDB.Driver;

namespace Akagi.Web.Services;

public interface IUserService
{
    Task<User> FindOrCreateUserAsync(string googleId, string name, string email);
}

public class UserService : IUserService
{
    private readonly IUserDatabase _database;

    public UserService(IUserDatabase database)
    {
        _database = database;
    }

    public async Task<User> FindOrCreateUserAsync(string googleId, string name, string email)
    {
        FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.GoogleId, googleId);
        List<User> users = await _database.GetDocumentsByPredicateAsync(filter);
        User? user = users.FirstOrDefault();

        if (user == null)
        {
            user = new User
            {
                GoogleId = googleId,
                Name = name,
                Email = email
            };
            await _database.SaveDocumentAsync(user);
        }

        return user;
    }
}
