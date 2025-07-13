namespace Akagi.Web.Services;

static class DependendyInjection
{
    public static void AddServices(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserState, UserState>();
    }
}
