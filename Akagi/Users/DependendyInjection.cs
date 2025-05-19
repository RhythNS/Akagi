using Microsoft.Extensions.DependencyInjection;

namespace Akagi.Users;

static class DependendyInjection
{
    public static void AddUsers(this IServiceCollection services)
    {
        services.AddSingleton<IUserDatabase, UserDatabase>();
    }
}
